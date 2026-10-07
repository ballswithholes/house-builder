// Styled transition markers (TransitionDef.marker): cave mouths, crypt doors, stairs down and raid portals, standing
// at the transition instead of the waymarker arch or posts (MapView.BuildTransition; the preview tool alike).
//
// The model is the prop library's (prop_cave_mouth, prop_crypt_door, prop_stairs_down, prop_raid_portal) when it exists,
// else a stand-in (PropModels.MarkerStandIn). It faces the camera (−Y in the world). cave and door stand with their
// pivot (the threshold) on the back edge of the transition rect, so the walk-in area lies in front of the mouth; stairs
// and portal stand on its centre. The portal glows: its light is always on (AlwaysLit); the others' lamps (the model's
// light anchors) light at night like the waymarker lanterns.
using UnityEngine;

namespace Lanternvale.Game
{
    internal sealed partial class Waymarker
    {
        /// <summary>A styled entrance's model (null for the arch / posts).</summary>
        public PropModel Prop;
        /// <summary>The marker style ("" for the arch / posts).</summary>
        public string Marker = "";
        /// <summary>The lights burn day and night (a portal's glow).</summary>
        public bool AlwaysLit;

        /// <summary>The prop art key of a marker style, or null (auto, none or unknown).</summary>
        public static string EntranceArt(string marker)
        {
            switch (marker ?? "")
            {
                case "cave": return "prop_cave_mouth";
                case "door": return "prop_crypt_door";
                case "stairs": return "prop_stairs_down";
                case "portal": return "prop_raid_portal";
                default: return null;
            }
        }

        /// <summary>The entrance model for a map's biome and the exit's target, when the prop library has a matching
        /// variant: an ember portal to the dragon's roost, a snowy cave mouth on the peaks and in ice caves, a golden-downs
        /// crypt door in the highlands. Else the plain <see cref="EntranceArt(string)"/>.</summary>
        public static string EntranceArt(string marker, string biome, string targetMap)
        {
            string art = EntranceArt(marker);
            if (art == null) return null;
            string t = targetMap ?? "", b = biome ?? "";
            string variant = null;
            if (marker == "portal" && (t.Contains("roost") || t.Contains("ashwyrm"))) variant = "prop_raid_portal_ember";
            else if (marker == "cave" && (b == Biomes.Peaks || b == Biomes.IceCave)) variant = "prop_cave_mouth_snow";
            else if (marker == "door" && b == Biomes.Highlands) variant = "prop_crypt_door_golden";
            return variant != null && PropModels.Has(variant) ? variant : art;
        }

        public static bool IsEntrance(string marker) => EntranceArt(marker) != null;

        /// <summary>Where a styled entrance's pivot stands: the transition centre, or (cave, door) its back edge.</summary>
        public static Vector2 EntranceAt(string marker, Vector2 pos, Vector2 size) =>
            marker == "cave" || marker == "door" ? new Vector2(pos.x, pos.y + size.y * 0.5f) : pos;

        /// <summary>A portal's glow: ember for the dragon's roost, else a soft spirit violet.</summary>
        public static Color PortalGlow(string targetMap)
        {
            string t = targetMap ?? "";
            return t.Contains("roost") || t.Contains("ashwyrm") ? new Color(1f, 0.6f, 0.3f) : new Color(0.74f, 0.58f, 1f);
        }

        /// <summary>
        /// Builds a styled entrance (marker cave | door | stairs | portal) under holder, at the holder's ground point,
        /// facing the camera. Lamps: the model's light anchors (a portal: its glow point). Starts unlit (a portal lit).
        /// </summary>
        public static Waymarker BuildEntrance(Transform holder, string marker, int seed, Color glow) =>
            BuildEntrance(holder, marker, seed, glow, null, null);

        /// <summary>As above, with the biome / target variant of the entrance model (see EntranceArt).</summary>
        public static Waymarker BuildEntrance(Transform holder, string marker, int seed, Color glow, string biome, string targetMap)
        {
            string art = EntranceArt(marker, biome, targetMap);
            var m = art != null && PropModels.Has(art) ? PropModels.Create(art, seed) : PropModels.MarkerStandIn(marker, seed, glow);
            var t = m.Root.transform;
            t.SetParent(holder, false);
            t.localPosition = Vector3.zero;
            t.localRotation = World3D.Upright;
            t.localScale = Vector3.one;
            var w = new Waymarker { Root = t, Prop = m, Marker = marker ?? "", AlwaysLit = marker == "portal" };
            bool any = false;
            for (int i = 0; i < m.Renderers.Count; i++)
            {
                var r = m.Renderers[i];
                if (r == null) continue;
                w.Frame.Add(r);
                if (!any) { w.Bounds = r.bounds; any = true; }
                else w.Bounds.Encapsulate(r.bounds);
            }
            if (!any) w.Bounds = new Bounds(World3D.At((Vector2)holder.position, 1f), new Vector3(2f, 2f, 2f));
            int lamps = w.AlwaysLit ? 1 : 2;
            for (int i = 0; i < m.LightAnchors.Count && i < lamps; i++) w.Lamps.Add(t.TransformPoint(m.LightAnchors[i]));
            if (w.AlwaysLit && w.Lamps.Count == 0) w.Lamps.Add(t.TransformPoint(new Vector3(0f, Mathf.Max(1f, m.Height * 0.45f), -0.4f)));
            // a soft contact shadow under the whole entrance
            var lb = m.LocalBounds;
            var anchor = new GameObject("Shadow");
            anchor.transform.SetParent(holder, false);
            anchor.transform.localPosition = new Vector3(lb.center.x, lb.center.z, 0f);
            MeshCache.AddShadow(anchor.transform, Mathf.Max(0.5f, lb.extents.x * 0.95f), Mathf.Max(0.4f, lb.extents.z * 0.9f), 0.3f);
            w.lit = !w.AlwaysLit;   // force the first SetLit through
            w.SetLit(w.AlwaysLit);
            return w;
        }
    }
}
