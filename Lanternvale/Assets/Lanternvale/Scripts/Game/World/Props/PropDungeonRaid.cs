// Expansion dungeon props, raids (props-dungeon): the spirit gate raid portal (moon-gate ring with a turning emissive
// swirl; also the "portal" transition marker), the Hollow Heart's corrupted heart-lantern dais, thorn walls, great-root
// arches, the huge dragon skull, the Ashwyrm's roost nest, Dragonsworn ash banners and a candle-lit altar.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game
{
    public static partial class PropModels
    {
        static void RegisterDungeonRaid(Dictionary<string, Recipe> r)
        {
            r["prop_raid_portal"] = RaidPortal;
            r["prop_raid_portal_ember"] = RaidPortal;   // the roost's gate: ember veil, basalt stones
            r["prop_hollow_heart_core"] = (art, seed) => DgGlow(art, seed, BuildHollowHeartCore, 2.2f, new Vector3(0f, HeartLanternY, HeartCoreZ));
            r["prop_thorn_wall"] = (art, seed) => Simple(art, seed, BuildThornWall, 2.0f);
            r["prop_root_arch"] = (art, seed) => DgGlow(art, seed, BuildRootArch, 2.4f, new Vector3(0f, 2.7f, 0f));
            r["prop_dragon_skull"] = (art, seed) => Simple(art, seed, BuildDragonSkull, 2.4f);
            r["prop_roost_nest"] = (art, seed) => DgGlow(art, seed, BuildRoostNest, 2.6f, new Vector3(0f, 0.7f, 0.7f));
            r["prop_ash_banner"] = (art, seed) => Swaying(art, seed, BuildAshBanner, 0.25f);
            r["prop_altar"] = (art, seed) => DgLit(art, seed, BuildAltar, 0.8f, null, 0f, new Vector3(0f, 1.25f, 0.05f));
        }

        // ------------------------------------------------------------------ raid portal: spirit moon gate (prop collider 6.4 × 1.3; as a marker none)

        const float PortalR = 1.75f, PortalCY = 2.25f;

        /// <summary>The gate's veil colours: eye, inner, outer, rim and the rune/lantern glow (spirit violet-teal, or ember).</summary>
        static Color[] PortalPalette(bool ember) => ember
            ? new[] { Paint.Hex("#FFF2C8"), Paint.Hex("#FFC35C"), Paint.Hex("#F0702E"), Paint.Hex("#6E2A2A"), Paint.Hex("#FF9A4A") }
            : new[] { Paint.Hex("#FFF6E2"), DgPal.Spirit, Paint.Hex("#A98BFF"), Paint.Hex("#3A3570"), DgPal.Spirit };

        static PropModel RaidPortal(string art, int seed)
        {
            int b = Bucket(seed);
            bool ember = art.EndsWith("_ember");
            var rig = new Rig(art);
            rig.Body(Cached(art + "#" + b, () => BuildRaidPortal(b, ember)));
            var swirl = rig.Part("Swirl", Cached(art + "#swirl", () => BuildPortalSwirl(ember)), new Vector3(0f, PortalCY, 0f), Quaternion.identity, Skin.PlainTwoSided);
            var motion = swirl.gameObject.AddComponent<PropMotion>();
            motion.SpinAxis = Vector3.forward;
            motion.SpinSpeed = ember ? -30f : -24f;
            rig.Light(new Vector3(0f, PortalCY, -0.2f));
            return rig.Done(2.0f);
        }

        static MeshBuilder BuildPortalSwirl(bool ember)
        {
            var pal = PortalPalette(ember);
            var mb = new MeshBuilder(VariantSeed(ember ? "prop_raid_portal_ember_swirl" : "prop_raid_portal_swirl", 0)) { Emission = 1f };
            const float R = PortalR - 0.12f;
            // a soft veil: concentric bands from a bright eye out to a deep rim
            const int seg = 28, bands = 6;
            for (int k = 0; k < bands; k++)
            {
                float r0 = R * k / bands, r1 = R * (k + 1) / bands;
                float t0 = (float)k / bands, t1 = (float)(k + 1) / bands;
                Color ColAt(float t) => t < 0.3f ? Color.Lerp(pal[0], pal[1], t / 0.3f) : t < 0.75f ? Color.Lerp(pal[1], pal[2], (t - 0.3f) / 0.45f) : Color.Lerp(pal[2], pal[3], (t - 0.75f) / 0.25f);
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                    var d1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    var z = Vector3.forward * 0.02f;
                    if (k == 0) Tri(mb, z, d0 * r1 + z, d1 * r1 + z, Vector3.back, ColAt(t1 * 0.5f));
                    else QuadC(mb, d0 * r0 + z, d0 * r1 + z, d1 * r1 + z, d1 * r0 + z, Vector3.back, Color.Lerp(ColAt(t0), ColAt(t1), 0.5f));
                }
            }
            // three thin, pale wisps curling out from the eye (the spin shows the veil turning)
            for (int k = 0; k < 3; k++)
            {
                const int ws = 12;
                for (int i = 0; i < ws; i++)
                {
                    float t0 = (float)i / ws, t1 = (float)(i + 1) / ws;
                    float r0 = 0.15f + t0 * (R - 0.35f), r1 = 0.15f + t1 * (R - 0.35f);
                    float a0 = (k * 120f + t0 * 200f) * Mathf.Deg2Rad, a1 = (k * 120f + t1 * 200f) * Mathf.Deg2Rad;
                    float w0 = 0.05f + Mathf.Sin(t0 * Mathf.PI) * 0.12f, w1 = 0.05f + Mathf.Sin(t1 * Mathf.PI) * 0.12f;
                    var c0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f);
                    var c1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f);
                    var z = Vector3.forward * -0.01f;
                    var col = Color.Lerp(pal[0], pal[1], 0.35f + 0.4f * (t0 + t1) * 0.5f);
                    QuadC(mb, c0 * (r0 - w0) + z, c0 * (r0 + w0) + z, c1 * (r1 + w1) + z, c1 * (r1 - w1) + z, Vector3.back, col);
                }
            }
            // the bright eye and a few motes
            Ngon(mb, new Vector3(0f, 0f, -0.02f), 0.2f, 10, Vector3.back, Vector3.up, 0f, pal[0]);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 1.7f;
                float rr = 0.45f + (i % 4) * 0.28f;
                mb.Color = i % 2 == 0 ? pal[0] : pal[1];
                Gem(mb, new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, -0.04f), 0.045f);
            }
            return mb;
        }

        static MeshBuilder BuildRaidPortal(int b, bool ember)
        {
            var mb = Builder(VariantSeed(ember ? "prop_raid_portal_ember" : "prop_raid_portal", b), 0.06f, 0.3f, 0.5f);
            var pal = PortalPalette(ember);
            // spirit gate: pale shrine stone; the roost's gate: soot-dark basalt with ember runes
            var stone = ember ? Paint.Hsv(Paint.Hex("#6A6066"), (b - 1.5f) * 4f) : Paint.Hsv(Color.Lerp(DgPal.Crypt, DgPal.Rock, 0.3f), (b - 1.5f) * 4f);
            var light = ember ? Paint.Hex("#8C8086") : DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.82f);
            // stepped plinth
            mb.Color = dark;
            mb.BoxOn(new Vector3(0f, 0f, 0.05f), new Vector3(4.2f, 0.2f, 1.1f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.2f, 0.05f), new Vector3(3.5f, 0.2f, 0.8f), light);
            // the moon ring: carved voussoir blocks round a circle, gold-capped keystones at the quarters
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / n;
                var c = new Vector3(Mathf.Cos(a) * (PortalR + 0.2f), PortalCY + Mathf.Sin(a) * (PortalR + 0.2f), 0.05f);
                if (c.y < 0.45f) continue;
                bool key = i % 4 == 0;
                mb.Color = key ? light : (i % 2 == 0 ? stone : Paint.Shade(stone, 0.93f));
                OBox(mb, c, new Vector3(key ? 0.62f : 0.5f, 0.74f, key ? 0.62f : 0.5f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
                if (key)
                {
                    mb.Color = Pal.Gold;
                    mb.Emission = 0.4f;
                    var outN = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    Gem(mb, c + outN * 0.33f + Vector3.back * 0.25f, 0.09f);
                    mb.Emission = 0f;
                }
            }
            // carved spirit runes glowing faintly on the front of the ring
            mb.Emission = 0.85f;
            for (int i = 0; i < n; i++)
            {
                if (i % 4 == 0) continue;
                float a = (i + 0.5f) * Mathf.PI * 2f / n;
                var c = new Vector3(Mathf.Cos(a) * (PortalR + 0.2f), PortalCY + Mathf.Sin(a) * (PortalR + 0.2f), -0.21f);
                if (c.y < 0.5f) continue;
                mb.Color = i % 2 == 0 ? pal[4] : Color.Lerp(pal[4], pal[0], 0.4f);
                OBox(mb, c, new Vector3(0.05f, 0.24f, 0.02f), Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg));
            }
            mb.Emission = 0f;
            // two lantern pillars flanking the gate
            for (int s = -1; s <= 1; s += 2)
            {
                float x = s * (PortalR + 1.05f);
                mb.Color = stone;
                mb.BoxOn(new Vector3(x, 0.2f, 0.05f), new Vector3(0.5f, 0.2f, 0.5f), light);
                mb.Push().Translate(x, 0f, 0.05f);
                mb.Lathe(new[] { new Vector2(0.17f, 0.4f), new Vector2(0.15f, 2.4f) }, 6, false, false, false, null, 30f);
                mb.Pop();
                mb.Color = light;
                mb.BoxOn(new Vector3(x, 2.4f, 0.05f), new Vector3(0.5f, 0.1f, 0.5f));
                mb.Emission = 0.95f;
                mb.Color = pal[4];
                mb.Box(new Vector3(x, 2.68f, 0.05f), new Vector3(0.3f, 0.36f, 0.3f));
                mb.Emission = 0f;
                mb.Color = stone;
                mb.Box(new Vector3(x, 2.68f, 0.05f), new Vector3(0.36f, 0.36f, 0.06f));
                mb.Color = dark;
                mb.Push().Translate(x, 0f, 0.05f);
                mb.Lathe(new[] { new Vector2(0.38f, 2.86f), new Vector2(0.3f, 2.98f), new Vector2(0f, 3.2f) }, 4, false, true, false, null, 45f);
                mb.Pop();
                // paper charms tied to the pillar
                Shide(mb, new Vector3(x - s * 0.18f, 2.1f, -0.15f), Vector3.right * s, Vector3.back, 0.36f);
            }
            // a straw rope across the ring's crown with paper streamers
            var ra = new Vector3(Mathf.Cos(130f * Mathf.Deg2Rad) * (PortalR + 0.2f), PortalCY + Mathf.Sin(130f * Mathf.Deg2Rad) * (PortalR + 0.2f), -0.3f);
            var rb = new Vector3(Mathf.Cos(50f * Mathf.Deg2Rad) * (PortalR + 0.2f), PortalCY + Mathf.Sin(50f * Mathf.Deg2Rad) * (PortalR + 0.2f), -0.3f);
            mb.Color = Pal.RopeStraw;
            Rope(mb, ra, rb, 0.35f, 0.06f, 10, 5);
            for (int i = 0; i < 3; i++)
                Shide(mb, SagPoint(ra, rb, 0.35f, 0.3f + i * 0.2f) + Vector3.down * 0.05f + Vector3.back * 0.05f, Vector3.right, Vector3.back, 0.36f);
            // moss on the old stones (spirit gate), drifted ash on the roost's
            var cap = ember ? ByNormal(DgPal.AshLight, DgPal.Ash, DgPal.Ash) : ByNormal(Pal.MossLight, Pal.Moss, Pal.Moss);
            FacetBlob(mb, new Vector3(-1.4f, 0.42f, -0.1f), new Vector3(0.4f, 0.05f, 0.3f), 0, 0.25f, b + 1, 0f, cap);
            FacetBlob(mb, new Vector3(0.6f, PortalCY + PortalR + 0.55f, 0.05f), new Vector3(0.36f, 0.07f, 0.3f), 0, 0.25f, b + 2, 0f, cap);
            return mb;
        }

        // ------------------------------------------------------------------ the Hollow Heart's core (collider 5.4 × 3.4; the root mound is set back so its front sits on the ellipse)

        const float HeartCoreZ = 1.0f, HeartLanternScale = 1.6f, HeartLanternY = 0.64f + (2.26f - 0.64f) * HeartLanternScale;

        static MeshBuilder BuildHollowHeartCore(int b)
        {
            var mb = Builder(VariantSeed("prop_hollow_heart_core", b), 0.06f, 0.35f, 0.5f);
            var stone = Color.Lerp(DgPal.CryptLight, Pal.Blight, 0.3f);
            var light = Color.Lerp(DgPal.CryptLight, Pal.Paper, 0.3f);
            var dark = Paint.Shade(stone, 0.78f);
            var bark = Color.Lerp(DgPal.Thorn, DgPal.Root, 0.35f);
            var barkL = Color.Lerp(DgPal.ThornLight, DgPal.RootLight, 0.3f);
            mb.Push().Translate(0f, 0f, HeartCoreZ);
            // the knot of the dead spirit tree's roots: a low mound of bark, heaving into the old lantern's foot
            FacetBlob(mb, new Vector3(0f, 0.18f, 0f), new Vector3(2.2f, 0.62f, 1.62f), 1, 0.16f, b + 1, 0.7f, ByNormal(barkL, bark, Paint.Shade(bark, 0.7f), 0.4f));
            FacetBlob(mb, new Vector3(0.1f, 0.62f, 0.15f), new Vector3(1.15f, 0.42f, 0.95f), 1, 0.2f, b + 2, 0.5f, ByNormal(barkL, bark, Paint.Shade(bark, 0.7f), 0.4f));
            // rose veins pulsing over the mound (the heart's corruption feeding through the roots)
            mb.Emission = 0.9f;
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + 15f + b * 11f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.74f);
                var p0 = dir * 0.6f + Vector3.up * 0.95f;
                var p1 = dir * 1.35f + Vector3.up * 0.62f + new Vector3(-dir.z, 0f, dir.x) * 0.15f;
                var p2 = dir * 2.0f + Vector3.up * 0.18f;
                mb.Color = i % 2 == 0 ? DgPal.Heart : Color.Lerp(DgPal.Heart, DgPal.Blight, 0.5f);
                Beam(mb, p0, p1, 0.07f, 0.04f);
                Beam(mb, p1, p2, 0.055f, 0.035f);
            }
            mb.Emission = 0f;
            // the old heart lantern: a great stone tōrō (1.6× a shrine lantern), its fire-box holding the corrupted heart-glow
            mb.Push().Translate(0f, 0.64f, 0f).Rotate(-4f, 0f, 6f).Scale(HeartLanternScale).Translate(0f, -0.64f, 0f);
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.62f, 0.64f), new Vector2(0.62f, 0.86f), new Vector2(0.4f, 0.98f), new Vector2(0.26f, 1.02f) }, 6, false, false, true);
            mb.Lathe(new[] { new Vector2(0.24f, 1.02f), new Vector2(0.22f, 1.65f) }, 6, false, false, false);
            mb.Lathe(new[] { new Vector2(0.22f, 1.64f), new Vector2(0.62f, 1.76f), new Vector2(0.62f, 1.86f), new Vector2(0.48f, 1.9f) }, 6, false, true, true);
            float y0 = 1.9f, y1 = 2.62f;
            float ap = 0.46f * Mathf.Cos(30f * Mathf.Deg2Rad);
            mb.Lathe(new[] { new Vector2(0.46f, y0), new Vector2(0.46f, y1) }, 6, false, false, false);
            for (int f = 0; f < 6; f++)
            {
                float a = (270f + f * 60f) * Mathf.Deg2Rad;
                var nrm = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var t = new Vector3(-nrm.z, 0f, nrm.x);
                var c = nrm * (ap + 0.005f) + Vector3.up * ((y0 + y1) * 0.5f);
                mb.Emission = 1f;
                QuadC(mb, c - t * 0.17f - Vector3.up * 0.28f, c - t * 0.17f + Vector3.up * 0.28f, c + t * 0.17f + Vector3.up * 0.28f, c + t * 0.17f - Vector3.up * 0.28f, nrm,
                      f == 0 ? DgPal.Heart : Color.Lerp(DgPal.Heart, DgPal.HeartDeep, 0.4f));
                mb.Emission = 0f;
            }
            // the heart itself in the front window, veined dark
            mb.Emission = 1f;
            mb.Color = Paint.Hex("#FFD0E4");
            Gem(mb, new Vector3(0f, (y0 + y1) * 0.5f, -ap - 0.02f), new Vector3(0.16f, 0.2f, 0.04f));
            mb.Emission = 0f;
            mb.Color = DgPal.Thorn;
            Beam(mb, new Vector3(-0.1f, y0 + 0.12f, -ap - 0.015f), new Vector3(0.05f, y1 - 0.08f, -ap - 0.015f), 0.025f, 0.01f, Vector3.back);
            Beam(mb, new Vector3(0.12f, y0 + 0.2f, -ap - 0.015f), new Vector3(-0.02f, y0 + 0.45f, -ap - 0.015f), 0.02f, 0.01f, Vector3.back);
            mb.Color = dark;
            mb.Lathe(new[] { new Vector2(0.48f, y1), new Vector2(0.78f, y1 + 0.08f), new Vector2(0.78f, y1 + 0.15f), new Vector2(0.6f, y1 + 0.28f), new Vector2(0.3f, y1 + 0.5f), new Vector2(0.12f, y1 + 0.55f) },
                     6, false, true, true, null, 0f);
            mb.Color = stone;
            mb.Lathe(new[] { new Vector2(0.12f, y1 + 0.54f), new Vector2(0.17f, y1 + 0.66f), new Vector2(0.1f, y1 + 0.8f), new Vector2(0f, y1 + 0.95f) }, 6, false, false, false);
            for (int i = 0; i < 3; i++)
            {
                float a = (230f + i * 40f) * Mathf.Deg2Rad;
                Shide(mb, new Vector3(Mathf.Cos(a) * 0.62f, 1.82f, Mathf.Sin(a) * 0.62f), new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), 0.3f);
            }
            mb.Pop();
            // black thorn-roots heaving out of the dais and coiling up the lantern to its fire-box
            float boxY = 0.64f + (y0 - 0.64f) * HeartLanternScale;
            for (int s = 0; s < 7; s++)
            {
                float a0 = (s * 51.4f + b * 20f) * Mathf.Deg2Rad;
                float reach = s % 2 == 0 ? 2.5f : 1.9f;
                Vector3 prev = new Vector3(Mathf.Cos(a0) * reach, 0.05f, Mathf.Sin(a0) * reach);
                int segs = s % 3 == 2 ? 6 : 10;
                for (int i = 1; i <= segs; i++)
                {
                    float t = i / 10f;
                    float a = a0 + t * 1.9f * (s % 2 == 0 ? 1f : -1f);
                    float rr = Mathf.Lerp(reach, 0.38f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.5f)));
                    float y = t < 0.4f ? 0.05f + Mathf.Sin(t / 0.4f * Mathf.PI) * 0.35f + t * 1.4f : 0.62f + (t - 0.4f) / 0.6f * (boxY - 0.62f);
                    var p = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                    mb.Color = i % 2 == 0 ? bark : barkL;
                    mb.Segment(prev, p, Mathf.Lerp(0.2f, 0.06f, t - 0.1f), Mathf.Lerp(0.2f, 0.06f, t), 6, false, true);
                    if (i % 2 == 1)
                    {
                        var outward = (new Vector3(p.x, 0f, p.z).normalized + Vector3.up * 0.4f + Vector3.back * 0.3f).normalized;
                        mb.Color = Paint.Hex("#20182A");
                        mb.Push().Translate(p).Rotate(Quaternion.FromToRotation(Vector3.up, outward));
                        mb.Cone(Vector3.zero, 0.06f, 0.28f, 4);
                        mb.Pop();
                    }
                    prev = p;
                }
            }
            // blight crystals breaking through the dais
            var body = Color.Lerp(Pal.Blight, Pal.Violet, 0.45f);
            for (int i = 0; i < 9; i++)
            {
                float a = (i * 40f + 25f + b * 13f) * Mathf.Deg2Rad;
                float rr = i % 2 == 0 ? 2.1f : 1.35f;
                float yb = i % 2 == 0 ? 0.3f : 0.62f;
                Crystal(mb, new Vector3(Mathf.Cos(a) * rr, yb - 0.05f, Mathf.Sin(a) * rr), 0.7f + (i % 3) * 0.35f, 0.14f + (i % 2) * 0.05f, Mathf.Sin(a) * 25f, -Mathf.Cos(a) * 25f, i * 31f, body, Paint.Hsv(Pal.Violet, i * 4f));
            }
            mb.Pop();
            return mb;
        }

        // ------------------------------------------------------------------ thorn wall (collider 4.4 × 1.2)

        static MeshBuilder BuildThornWall(int b)
        {
            var mb = Builder(VariantSeed("prop_thorn_wall", b), 0.07f, 0.35f, 0.6f);
            var thorn = DgPal.Thorn;
            var thornL = DgPal.ThornLight;
            var earth = Paint.Hex("#3E3440");
            FacetBlob(mb, new Vector3(0f, 0.05f, 0.25f), new Vector3(2.05f, 0.3f, 0.5f), 1, 0.2f, b + 1, 0.5f, ByNormal(Paint.Shade(earth, 1.2f), earth, earth));
            // tangled bramble arcs: each vine rises from the ground, loops over and dives back down
            for (int v = 0; v < 16; v++)
            {
                float x0 = -1.9f + mb.Random01() * 3.8f;
                float span = 0.8f + mb.Random01() * 1.4f;
                float dir = v % 2 == 0 ? 1f : -1f;
                float x1 = Mathf.Clamp(x0 + dir * span, -1.95f, 1.95f);
                float h = 1.2f + mb.Random01() * 1.5f;
                float z = 0.05f + mb.Random01() * 0.55f;
                float zLean = (mb.Random01() - 0.5f) * 0.5f;
                Vector3 prev = new Vector3(x0, 0f, z);
                const int seg = 8;
                for (int i = 1; i <= seg; i++)
                {
                    float t = (float)i / seg;
                    var p = new Vector3(Mathf.Lerp(x0, x1, t), Mathf.Sin(t * Mathf.PI) * h, z + Mathf.Sin(t * Mathf.PI) * zLean);
                    float r = Mathf.Lerp(0.09f, 0.04f, t);
                    mb.Color = (i + v) % 3 == 0 ? thornL : thorn;
                    mb.Segment(prev, p, r + 0.01f, r, 5, false, true);
                    // thorns spiking outwards
                    var d = (p - prev).normalized;
                    var side = Vector3.Cross(d, (v + i) % 2 == 0 ? Vector3.back : Vector3.up).normalized;
                    mb.Color = Paint.Hex("#20182A");
                    mb.Push().Translate(p).Rotate(Quaternion.FromToRotation(Vector3.up, side + Vector3.back * 0.4f));
                    mb.Cone(Vector3.zero, 0.04f, 0.2f, 4);
                    mb.Pop();
                    prev = p;
                }
            }
            // a few dead leaves, glowing violet buds and blight crystals
            mb.Emission = 0.9f;
            for (int i = 0; i < 6; i++)
            {
                mb.Color = i % 2 == 0 ? DgPal.Blight : Paint.Hex("#E2A6FF");
                Gem(mb, new Vector3(-1.6f + i * 0.64f + (b - 1.5f) * 0.1f, 0.9f + (i % 3) * 0.55f, -0.08f - (i % 2) * 0.06f), 0.07f);
            }
            mb.Emission = 0f;
            var body = Color.Lerp(Pal.Blight, Pal.Violet, 0.45f);
            Crystal(mb, new Vector3(-1.25f, 0.05f, -0.1f), 0.7f, 0.12f, -14f, 18f, 10f, body, Pal.Violet);
            Crystal(mb, new Vector3(1.4f, 0.05f, -0.05f), 0.55f, 0.1f, -10f, -22f, 40f, body, Pal.Violet);
            if (b % 2 == 0) Crystal(mb, new Vector3(0.2f, 0.05f, -0.15f), 0.42f, 0.09f, -20f, 6f, 70f, body, Pal.Violet);
            for (int i = 0; i < 6; i++)
            {
                mb.Color = i % 2 == 0 ? Paint.Hex("#6A5060") : Paint.Hex("#7E5A50");
                Leaf(mb, new Vector3(-1.5f + i * 0.6f, 0.5f + (i % 3) * 0.6f, -0.05f), new Vector3(0.3f, -0.5f, 0f), Vector3.back, 0.18f, mb.Color);
            }
            return mb;
        }

        // ------------------------------------------------------------------ great-root arch (walk-through: no collider; the feet stand at x = ±2.1)

        static MeshBuilder BuildRootArch(int b)
        {
            var mb = Builder(VariantSeed("prop_root_arch", b), 0.07f, 0.35f, 0.8f);
            var bark = Paint.Hsv(DgPal.Root, (b - 1.5f) * 4f);
            bool blighted = b % 2 == 1;
            const float span = 2.1f, H = 4.2f;
            // two twisted strands per side rising and meeting overhead (a squashed half-ellipse)
            for (int s = 0; s < 3; s++)
            {
                float off = (s - 1) * 0.22f;
                Vector3 prev = Vector3.zero;
                const int seg = 18;
                for (int i = 0; i <= seg; i++)
                {
                    float t = (float)i / seg;
                    float a = Mathf.PI * (1f - t);
                    // strands braid round each other (twist about the arch line), the left side a little lower and leaning
                    float ph = t * Mathf.PI * 4f + s * 2.1f;
                    float tw = 0.26f * (0.6f + 0.4f * Mathf.Abs(t - 0.5f) * 2f);
                    float lean = (1f - t) * 0.25f;
                    var p = new Vector3(Mathf.Cos(a) * span + Mathf.Cos(ph) * tw * 0.6f - lean,
                                        Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a)), 0.8f) * (H - 0.4f - (1f - t) * 0.3f) + 0.05f + Mathf.Sin(ph) * tw * 0.5f,
                                        Mathf.Sin(ph) * tw + 0.1f);
                    if (i > 0)
                    {
                        float k = Mathf.Abs(t - 0.5f) * 2f;   // thicker at the feet
                        float rad = Mathf.Lerp(0.13f, 0.34f, k * k) * (s == 1 ? 1.15f : 0.9f) * (1f + 0.12f * Mathf.Sin(i * 1.7f + s));
                        mb.Color = (i + s) % 4 == 0 ? Paint.Shade(bark, 1.08f) : (s == 1 ? Paint.Shade(bark, 0.92f) : bark);
                        mb.Segment(prev, p, rad, rad * 0.95f, 6, false, true);
                        // knots and side rootlets now and then
                        if ((i + s * 5) % 7 == 3)
                        {
                            mb.Color = Paint.Shade(bark, 0.85f);
                            Gem(mb, p + Vector3.back * rad * 0.7f, rad * 0.55f);
                            var side = (p - prev).normalized;
                            mb.Segment(p, p + new Vector3(-side.y, side.x, 0f) * 0.45f * (s == 0 ? 1f : -1f) + Vector3.back * 0.15f, rad * 0.4f, 0.02f, 4);
                        }
                    }
                    prev = p;
                }
            }
            // buttress roots at both feet
            for (int sd = -1; sd <= 1; sd += 2)
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f + 45f + b * 15f) * Mathf.Deg2Rad;
                    var foot = new Vector3(sd * span, 0f, 0.1f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * 0.6f);
                    mb.Color = Paint.Shade(bark, 0.9f);
                    mb.Segment(foot + Vector3.up * 0.5f, foot + dir * 0.85f + Vector3.up * 0.03f, 0.16f, 0.04f, 5);
                }
            // moss, hanging rootlets with little paper lanterns, glowing fungi
            var moss = Pal.Moss;
            var mossCol = ByNormal(Paint.Shade(moss, 1.15f), moss, Paint.Shade(moss, 0.8f));
            FacetBlob(mb, new Vector3(-0.4f, H - 0.15f, 0.12f), new Vector3(0.7f, 0.16f, 0.35f), 0, 0.3f, b + 1, 0f, mossCol);
            FacetBlob(mb, new Vector3(-span + 0.2f, 1.6f, -0.05f), new Vector3(0.2f, 0.4f, 0.18f), 0, 0.3f, b + 2, 0f, mossCol);
            FacetBlob(mb, new Vector3(span - 0.1f, 0.9f, -0.05f), new Vector3(0.22f, 0.35f, 0.16f), 0, 0.3f, b + 3, 0f, mossCol);
            mb.WindGradient = true; mb.WindY0 = H; mb.WindY1 = H - 1.5f; mb.Wind = 0.5f;
            for (int i = 0; i < 5; i++)
            {
                float x = -1.2f + i * 0.6f;
                float top = Mathf.Pow(Mathf.Sqrt(Mathf.Max(0f, 1f - (x / span) * (x / span))), 0.8f) * (H - 0.4f) - 0.1f;
                mb.Color = DgPal.RootLight;
                float len = 0.6f + ((i * 3 + b) % 3) * 0.35f;
                mb.Segment(new Vector3(x, top, 0.05f), new Vector3(x + 0.05f, top - len, 0.0f), 0.03f, 0.01f, 4);
                if (i == 1 || i == 3) PaperLantern(mb, new Vector3(x + 0.05f, top - len - 0.2f, 0f), 0.13f, 0.28f, Pal.Paper, true, Pal.TimberDark);
            }
            mb.Wind = 0f; mb.WindGradient = false;
            LittleMushroom(mb, new Vector3(-span - 0.3f, 0.05f, -0.25f), 0.22f, 0.12f, DgPal.Spirit, 10f, 20f, 0.85f);
            LittleMushroom(mb, new Vector3(-span + 0.35f, 0.05f, -0.3f), 0.15f, 0.08f, DgPal.Spirit, -12f, 70f, 0.85f);
            LittleMushroom(mb, new Vector3(span + 0.3f, 0.05f, -0.2f), 0.18f, 0.1f, DgPal.Spirit, -8f, 50f, 0.85f);
            if (blighted)
            {
                // the Hollow's corruption creeping up one side
                var body = Color.Lerp(Pal.Blight, Pal.Violet, 0.45f);
                Crystal(mb, new Vector3(span + 0.25f, 0.05f, 0.25f), 0.8f, 0.13f, 10f, -24f, 0f, body, Pal.Violet);
                Crystal(mb, new Vector3(span - 0.2f, 0.05f, -0.3f), 0.5f, 0.1f, -20f, 10f, 30f, body, Pal.Violet);
                mb.Emission = 0.8f;
                mb.Color = DgPal.Blight;
                for (int i = 0; i < 4; i++) Gem(mb, new Vector3(span - 0.12f + (i % 2) * 0.1f, 1.0f + i * 0.45f, -0.18f), 0.06f);
                mb.Emission = 0f;
            }
            return mb;
        }

        // ------------------------------------------------------------------ dragon skull (collider 6.0 × 2.8; snout turned to the front-left)

        static MeshBuilder BuildDragonSkull(int b)
        {
            var mb = Builder(VariantSeed("prop_dragon_skull", b), 0.06f, 0.4f, 0.9f);
            var bone = Paint.Hsv(DgPal.Bone, (b - 1.5f) * 3f);
            var boneD = DgPal.BoneDark;
            var soot = DgPal.Soot;
            var ash = DgPal.Ash;
            // ash drift it lies in
            FacetBlob(mb, new Vector3(0.1f, 0.0f, 0.3f), new Vector3(2.6f, 0.22f, 1.3f), 1, 0.2f, b + 1, 0.6f, ByNormal(DgPal.AshLight, ash, ash));
            mb.Push().Translate(0.35f, 0f, 0.35f).Rotate(0f, -28f, 0f);
            var boneCol = ByNormal(Paint.Shade(bone, 1.05f), bone, Color.Lerp(boneD, soot, 0.4f), 0.3f);
            // cranium
            FacetBlob(mb, new Vector3(1.0f, 1.45f, 0f), new Vector3(1.15f, 1.0f, 0.95f), 1, 0.1f, b + 2, 0.15f, boneCol);
            // upper jaw: a lofted, tapering snout running along -X
            (float x, float w, float h, float cy)[] st =
            {
                (0.9f, 0.95f, 0.95f, 1.25f), (0.1f, 0.85f, 0.8f, 1.05f), (-0.8f, 0.68f, 0.62f, 0.88f), (-1.7f, 0.55f, 0.5f, 0.74f), (-2.45f, 0.42f, 0.38f, 0.66f), (-2.75f, 0.2f, 0.2f, 0.62f),
            };
            var rings = new List<Vector3[]>();
            foreach (var r in st)
            {
                var ring = new Vector3[6];
                for (int j = 0; j < 6; j++)
                {
                    float a = (j * 60f + 30f) * Mathf.Deg2Rad;
                    float zz = Mathf.Cos(a) * r.w;
                    float yy = Mathf.Sin(a) * r.h * (Mathf.Sin(a) < 0f ? 0.55f : 1f);
                    ring[j] = new Vector3(r.x, r.cy + yy, zz);
                }
                rings.Add(ring);
            }
            Loft(mb, rings, false, true, boneCol);
            // brow ridges, eye sockets (both sides), nostrils
            for (int sd = -1; sd <= 1; sd += 2)
            {
                FacetBlob(mb, new Vector3(0.45f, 1.85f, sd * 0.62f), new Vector3(0.55f, 0.2f, 0.28f), 0, 0.15f, b + 5 + sd, 0f, boneCol);
                mb.Color = DgPal.Void;
                Gem(mb, new Vector3(0.42f, 1.55f, sd * 0.86f), new Vector3(0.3f, 0.24f, 0.12f));
                mb.Color = Color.Lerp(soot, DgPal.Void, 0.5f);
                Gem(mb, new Vector3(-2.5f, 0.92f, sd * 0.2f), new Vector3(0.12f, 0.06f, 0.08f));
                // the cheek arch
                mb.Color = bone;
                mb.Segment(new Vector3(1.3f, 1.05f, sd * 0.88f), new Vector3(-0.3f, 0.85f, sd * 0.78f), 0.12f, 0.08f, 5);
            }
            // great horns sweeping back and up, and a crest of smaller spikes
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3[] hp = { new Vector3(1.3f, 2.05f, sd * 0.55f), new Vector3(2.1f, 2.45f, sd * 0.85f), new Vector3(2.85f, 3.05f, sd * 1.0f), new Vector3(3.2f, 3.75f, sd * 0.95f), new Vector3(3.15f, 4.2f, sd * 0.8f) };
                float[] hr = { 0.28f, 0.22f, 0.15f, 0.08f, 0.01f };
                for (int i = 0; i < hp.Length - 1; i++)
                {
                    mb.Color = i < 2 ? Color.Lerp(bone, Pal.Gold, 0.15f) : Color.Lerp(bone, soot, i * 0.15f);
                    mb.Segment(hp[i], hp[i + 1], hr[i], hr[i + 1], 6, false, true);
                }
                mb.Color = boneD;
                mb.Segment(new Vector3(1.6f, 1.2f, sd * 0.8f), new Vector3(2.4f, 0.95f, sd * 1.1f), 0.14f, 0.02f, 5);
            }
            mb.Color = Paint.Shade(bone, 0.95f);
            for (int i = 0; i < 4; i++)
            {
                float x = 1.6f - i * 0.45f;
                mb.Push().Translate(x, 2.2f - i * 0.18f, 0f).Rotate(0f, 0f, 25f);
                mb.Cone(Vector3.zero, 0.12f - i * 0.02f, 0.45f - i * 0.07f, 4);
                mb.Pop();
            }
            // teeth along the upper jaw
            mb.Color = Paint.Hex("#F6EFDC");
            for (int sd = -1; sd <= 1; sd += 2)
                for (int i = 0; i < 6; i++)
                {
                    float x = -2.4f + i * 0.42f;
                    float w = Mathf.Lerp(0.3f, 0.75f, i / 5f);
                    mb.Push().Translate(x, Mathf.Lerp(0.55f, 0.72f, i / 5f), sd * w).Rotate(180f, 0f, 0f);
                    mb.Cone(Vector3.zero, 0.06f + (i == 1 ? 0.03f : 0f), 0.22f + (i == 1 ? 0.2f : 0f), 4);
                    mb.Pop();
                }
            // the lower jaw fallen open in the ash
            mb.Color = Color.Lerp(bone, boneD, 0.3f);
            for (int sd = -1; sd <= 1; sd += 2)
            {
                mb.Segment(new Vector3(0.8f, 0.3f, sd * 0.7f), new Vector3(-1.6f, 0.12f, sd * 0.55f), 0.16f, 0.1f, 5);
                mb.Segment(new Vector3(-1.6f, 0.12f, sd * 0.55f), new Vector3(-2.3f, 0.1f, sd * 0.15f), 0.1f, 0.08f, 5);
                mb.Color = Paint.Hex("#F6EFDC");
                for (int i = 0; i < 4; i++)
                {
                    float t = i / 4f;
                    var p = Vector3.Lerp(new Vector3(-1.4f, 0.22f, sd * 0.53f), new Vector3(0.4f, 0.32f, sd * 0.66f), t);
                    mb.Cone(p, 0.05f, 0.16f, 4);
                }
                mb.Color = Color.Lerp(bone, boneD, 0.3f);
            }
            // soot and a crack across the crown
            mb.Color = soot;
            Beam(mb, new Vector3(0.5f, 2.38f, 0.1f), new Vector3(1.2f, 2.3f, -0.4f), 0.05f, 0.02f);
            Beam(mb, new Vector3(1.2f, 2.3f, -0.4f), new Vector3(1.5f, 1.9f, -0.85f), 0.04f, 0.02f);
            mb.Pop();
            // scattered bones and a broken rib
            DgBone(mb, new Vector3(-1.9f, 0.08f, -0.45f), new Vector3(-1.0f, 0.06f, -0.85f), 0.06f, bone);
            mb.Color = bone;
            mb.Segment(new Vector3(2.0f, 0.05f, -0.4f), new Vector3(2.3f, 0.8f, -0.2f), 0.09f, 0.06f, 5);
            mb.Segment(new Vector3(2.3f, 0.8f, -0.2f), new Vector3(2.15f, 1.3f, 0.05f), 0.06f, 0.03f, 5);
            return mb;
        }

        // ------------------------------------------------------------------ roost nest (collider 6.2 × 3.4; nest set back so its front rim sits on the ellipse)

        static MeshBuilder BuildRoostNest(int b)
        {
            var mb = Builder(VariantSeed("prop_roost_nest", b), 0.08f, 0.35f, 0.5f);
            var charred = DgPal.Soot;
            var burnt = Paint.Hex("#5C4436");
            var wood = DgPal.WoodDark;
            const float cz = 0.75f, rx = 2.55f, rz = 1.95f;
            // ash floor of the nest
            var lining = Color.Lerp(DgPal.AshLight, Pal.Thatch, 0.35f);
            FacetBlob(mb, new Vector3(0f, -0.02f, cz), new Vector3(rx * 0.95f, 0.16f, rz * 0.95f), 1, 0.1f, b + 1, 0.8f, ByNormal(lining, Paint.Shade(lining, 0.85f), DgPal.Ash));
            // down feathers and charred straw in the lining
            for (int i = 0; i < 18; i++)
            {
                float a = (i * 137.5f + b * 30f) * Mathf.Deg2Rad;
                float rr = 0.4f + (i % 6) * 0.3f;
                var p = new Vector3(Mathf.Cos(a) * rr, 0.13f, cz + Mathf.Sin(a) * rr * 0.75f);
                mb.Color = i % 3 == 0 ? Pal.Cream : (i % 3 == 1 ? Paint.Hex("#D9C7A8") : Pal.ThatchDark);
                if (i % 3 == 2) Beam(mb, p, p + new Vector3(Mathf.Sin(a) * 0.4f, 0.02f, Mathf.Cos(a) * 0.3f), 0.03f, 0.02f);
                else Gem(mb, p, new Vector3(0.12f, 0.03f, 0.07f));
            }
            // the rim: three layers of charred branches laid round the ring, crossing each other
            for (int layer = 0; layer < 3; layer++)
            {
                int n = 22 - layer * 3;
                for (int i = 0; i < n; i++)
                {
                    float a = (i + layer * 0.37f) * Mathf.PI * 2f / n;
                    float len = 1.1f + mb.Random01() * 0.7f;
                    var c = new Vector3(Mathf.Cos(a) * (rx - layer * 0.12f), 0.18f + layer * 0.3f + mb.Random01() * 0.08f, cz + Mathf.Sin(a) * (rz - layer * 0.1f));
                    var tan = new Vector3(-Mathf.Sin(a) * rx, 0f, Mathf.Cos(a) * rz).normalized;
                    var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float tilt = (mb.Random01() - 0.5f) * 0.5f;
                    var d = (tan + radial * tilt * 0.6f + Vector3.up * tilt * 0.4f).normalized;
                    var p0 = c - d * len * 0.5f;
                    var p1 = c + d * len * 0.5f;
                    float r = 0.07f + mb.Random01() * 0.05f;
                    mb.Color = (i + layer) % 3 == 0 ? burnt : ((i + layer) % 3 == 1 ? charred : wood);
                    mb.Segment(p0, p1, r, r * 0.7f, 5, false, true);
                    // a forked twig off some branches, ember-tipped now and then
                    if (i % 3 == 0)
                    {
                        var twig = p1 + (d + Vector3.up * 0.6f + radial * 0.3f).normalized * 0.45f;
                        mb.Segment(p1 - d * 0.1f, twig, r * 0.6f, 0.015f, 4);
                        if (i % 2 == 0)
                        {
                            mb.Emission = 0.9f;
                            mb.Color = Pal.Ember;
                            Gem(mb, twig, 0.04f);
                            mb.Emission = 0f;
                        }
                    }
                }
            }
            // bones woven into the rim: a big rib arc and a horned skull
            var bone = DgPal.Bone;
            mb.Color = bone;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 prev = new Vector3(sd * 1.2f, 0.1f, cz - rz + 0.1f);
                for (int i = 1; i <= 4; i++)
                {
                    float t = i / 4f;
                    var p = new Vector3(sd * (1.2f - t * 0.5f), 0.1f + Mathf.Sin(t * Mathf.PI * 0.8f) * 1.3f, cz - rz + 0.1f + t * 0.5f);
                    mb.Segment(prev, p, 0.07f, 0.06f, 5);
                    prev = p;
                }
            }
            DgSkull(mb, new Vector3(1.9f, 0.55f, cz - rz * 0.7f), 2.4f, 200f, bone, -10f);
            DgBone(mb, new Vector3(-2.0f, 0.65f, cz - 1.0f), new Vector3(-1.2f, 0.75f, cz - 1.6f), 0.05f, bone);
            // three great eggs nestled in the ash, cracked with ember light
            (Vector3 p, float s, float tilt)[] eggs = { (new Vector3(-0.4f, 0f, cz + 0.1f), 0.75f, 8f), (new Vector3(0.55f, 0f, cz + 0.3f), 0.66f, -12f), (new Vector3(0.05f, 0f, cz + 0.95f), 0.6f, 4f) };
            for (int i = 0; i < eggs.Length; i++)
            {
                var e = eggs[i];
                mb.Push().Translate(e.p + Vector3.up * 0.12f).Rotate(e.tilt, i * 40f, e.tilt * 0.5f);
                mb.Color = i == 1 ? Paint.Hex("#4A3A3A") : Paint.Hex("#3A3236");
                mb.Lathe(new[] { new Vector2(0f, 0f), new Vector2(e.s * 0.6f, e.s * 0.15f), new Vector2(e.s * 0.72f, e.s * 0.6f), new Vector2(e.s * 0.55f, e.s * 1.15f), new Vector2(e.s * 0.25f, e.s * 1.45f), new Vector2(0f, e.s * 1.55f) },
                         8, true, false, false, new[] { Paint.Hex("#2E2628"), mb.Color, mb.Color, Paint.Shade(mb.Color, 1.1f), Paint.Shade(mb.Color, 1.15f), Paint.Shade(mb.Color, 1.2f) });
                mb.Emission = 1f;
                mb.Color = Color.Lerp(Pal.Ember, Pal.FireCore, 0.3f);
                for (int k = 0; k < 3; k++)
                {
                    float a = (k * 120f + i * 30f) * Mathf.Deg2Rad;
                    var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var p0 = n * e.s * 0.73f + Vector3.up * e.s * 0.45f;
                    var p1 = n * e.s * 0.62f + Vector3.up * e.s * 0.95f + new Vector3(-n.z, 0f, n.x) * 0.1f;
                    Beam(mb, p0 + n * 0.02f, p1 + n * 0.02f, 0.07f, 0.03f, n);
                    Beam(mb, p1 + n * 0.02f, p1 + n * 0.02f + Vector3.up * e.s * 0.25f - new Vector3(-n.z, 0f, n.x) * 0.12f, 0.05f, 0.03f, n);
                }
                mb.Emission = 0f;
                mb.Pop();
            }
            return mb;
        }

        // ------------------------------------------------------------------ ash banner (collider 0.6 × 0.45; cloth sways)

        static MeshBuilder BuildAshBanner(int b)
        {
            var mb = Builder(VariantSeed("prop_ash_banner", b), 0.06f, 0.3f, 0.4f);
            var cloth = Paint.Hsv(Paint.Hex("#4A4148"), (b - 1.5f) * 6f);
            var ember = Paint.Hex("#E2552E");
            // a stone-weighted foot, the pole and a horned crossbar
            DgRock(mb, new Vector3(0f, 0.1f, 0.05f), new Vector3(0.3f, 0.16f, 0.22f), b + 1, DgPal.Ash, DgPal.AshLight, 0.5f, 0);
            mb.Color = DgPal.Soot;
            mb.Segment(new Vector3(0f, 0f, 0.05f), new Vector3(0f, 4.3f, 0.05f), 0.06f, 0.045f, 6);
            mb.Color = DgPal.IronOld;
            mb.Box(new Vector3(0f, 3.95f, 0.05f), new Vector3(1.5f, 0.08f, 0.08f));
            mb.Color = DgPal.BoneDark;
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Segment(new Vector3(s * 0.72f, 3.95f, 0.05f), new Vector3(s * 0.88f, 4.2f, 0.05f), 0.06f, 0.04f, 5);
                mb.Segment(new Vector3(s * 0.88f, 4.2f, 0.05f), new Vector3(s * 0.8f, 4.45f, 0.05f), 0.04f, 0.01f, 5);
            }
            mb.Color = DgPal.Bone;
            mb.Lathe(new[] { new Vector2(0.07f, 4.3f), new Vector2(0.03f, 4.6f), new Vector2(0f, 4.72f) }, 5, false, true, false);
            // the cloth: a tall panel hanging from the crossbar, torn into tongues at the foot, scorched
            mb.WindGradient = true; mb.WindY0 = 3.9f; mb.WindY1 = 1.5f; mb.Wind = 1.3f;
            const float top = 3.88f, bottom = 1.5f, hw = 0.62f;
            const int strips = 5;
            for (int i = 0; i < strips; i++)
            {
                float x0 = -hw + 2f * hw * i / strips, x1 = -hw + 2f * hw * (i + 1) / strips;
                float tear = bottom + ((i * 7 + b * 3) % 4) * 0.12f;
                float mid = (x0 + x1) * 0.5f;
                float wave = Mathf.Sin(i * 1.3f + b) * 0.04f;
                var col = i % 2 == 0 ? cloth : Paint.Shade(cloth, 0.93f);
                QuadC(mb, new Vector3(x0, top, -0.02f), new Vector3(x1, top, -0.02f), new Vector3(x1, tear + 0.25f, -0.04f + wave), new Vector3(x0, tear + 0.22f, -0.04f + wave), Vector3.back, col);
                Tri(mb, new Vector3(x0, tear + 0.22f, -0.04f + wave), new Vector3(x1, tear + 0.25f, -0.04f + wave), new Vector3(mid, tear - 0.12f, -0.05f + wave), Vector3.back, Paint.Shade(col, 0.75f));
                // the back of the cloth
                QuadC(mb, new Vector3(x0, top, 0.0f), new Vector3(x1, top, 0.0f), new Vector3(x1, tear + 0.25f, -0.02f + wave), new Vector3(x0, tear + 0.22f, -0.02f + wave), Vector3.forward, Paint.Shade(col, 0.85f));
                Tri(mb, new Vector3(x0, tear + 0.22f, -0.02f + wave), new Vector3(x1, tear + 0.25f, -0.02f + wave), new Vector3(mid, tear - 0.12f, -0.03f + wave), Vector3.forward, Paint.Shade(col, 0.7f));
            }
            // the Dragonsworn sigil: an ember-red wyrm's head in a ring, a band of gold at the hem
            mb.Emission = 0.25f;
            mb.Color = ember;
            Ngon(mb, new Vector3(0f, 3.05f, -0.055f), 0.36f, 14, Vector3.back, Vector3.up, 0f, ember);
            mb.Color = cloth;
            Ngon(mb, new Vector3(0f, 3.05f, -0.06f), 0.29f, 14, Vector3.back, Vector3.up, 0f, cloth);
            mb.Color = ember;
            Tri(mb, new Vector3(-0.2f, 3.15f, -0.065f), new Vector3(0.22f, 2.92f, -0.065f), new Vector3(-0.05f, 2.86f, -0.065f), Vector3.back, ember);
            Tri(mb, new Vector3(-0.2f, 3.15f, -0.065f), new Vector3(-0.08f, 3.32f, -0.065f), new Vector3(0.0f, 3.1f, -0.065f), Vector3.back, ember);
            Tri(mb, new Vector3(-0.05f, 3.2f, -0.065f), new Vector3(0.12f, 3.36f, -0.065f), new Vector3(0.06f, 3.12f, -0.065f), Vector3.back, ember);
            mb.Emission = 0f;
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(0f, 3.78f, -0.05f), new Vector3(1.24f, 0.06f, 0.01f));
            mb.Color = DgPal.Soot;
            for (int i = 0; i < 3; i++) Gem(mb, new Vector3(-0.4f + i * 0.4f, 1.9f + (i % 2) * 0.15f, -0.06f), new Vector3(0.12f, 0.08f, 0.01f));
            mb.Wind = 0f; mb.WindGradient = false;
            return mb;
        }

        // ------------------------------------------------------------------ altar (collider 2.4 × 1.2; candles lit)

        static MeshBuilder BuildAltar(int b, bool lit)
        {
            var mb = Builder(VariantSeed("prop_altar", b), 0.06f, 0.3f, 0.4f);
            var stone = Paint.Hsv(DgPal.Crypt, (b - 1.5f) * 4f);
            var light = DgPal.CryptLight;
            var dark = Paint.Shade(stone, 0.82f);
            // a low step, the altar block with a projecting top slab
            mb.Color = dark;
            mb.BoxOn(new Vector3(0f, 0f, 0.08f), new Vector3(1.9f, 0.14f, 0.92f), stone);
            mb.Color = stone;
            mb.BoxOn(new Vector3(0f, 0.14f, 0.1f), new Vector3(1.5f, 0.74f, 0.66f));
            mb.Color = light;
            mb.BoxOn(new Vector3(0f, 0.88f, 0.1f), new Vector3(1.66f, 0.12f, 0.78f));
            // carved front: a lantern in a round frame between two pilasters
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = light;
                mb.Box(new Vector3(s * 0.62f, 0.51f, -0.24f), new Vector3(0.14f, 0.74f, 0.04f));
            }
            mb.Color = dark;
            Ngon(mb, new Vector3(0f, 0.52f, -0.235f), 0.24f, 12, Vector3.back, Vector3.up, 0f, dark);
            mb.Color = light;
            Slab(mb, new[] { new Vector3(-0.08f, 0.4f, -0.24f), new Vector3(-0.08f, 0.6f, -0.24f), new Vector3(0f, 0.68f, -0.24f), new Vector3(0.08f, 0.6f, -0.24f), new Vector3(0.08f, 0.4f, -0.24f) }, Vector3.back, 0.015f);
            // the vermilion runner hanging over the front
            mb.Color = Pal.Vermilion;
            mb.Box(new Vector3(0f, 1.005f, 0.1f), new Vector3(0.5f, 0.01f, 0.8f));
            Slab(mb, new[] { new Vector3(-0.25f, 1.0f, -0.305f), new Vector3(0.25f, 1.0f, -0.305f), new Vector3(0.25f, 0.68f, -0.305f), new Vector3(0f, 0.6f, -0.305f), new Vector3(-0.25f, 0.68f, -0.305f) }, Vector3.back, 0.01f);
            mb.Color = Pal.Gold;
            mb.Box(new Vector3(0f, 0.72f, -0.312f), new Vector3(0.4f, 0.02f, 0.005f));
            // offerings: a bowl of incense, a little bell, fruit; candles at both ends
            mb.Color = Pal.Brass;
            mb.Lathe(new[] { new Vector2(0.08f, 1.0f), new Vector2(0.16f, 1.06f), new Vector2(0.17f, 1.12f) }, 8, false, true, false);
            mb.Color = DgPal.AshLight;
            mb.Disc(new Vector3(0f, 1.1f, 0f), 0.15f, 8);
            mb.Color = Paint.Hex("#A85A3C");
            for (int i = 0; i < 3; i++) mb.Segment(new Vector3(-0.04f + i * 0.04f, 1.1f, 0.0f), new Vector3(-0.06f + i * 0.06f, 1.32f, 0.02f), 0.007f, 0.006f, 3);
            mb.Color = Pal.Apple;
            Gem(mb, new Vector3(-0.38f, 1.06f, 0.18f), 0.07f);
            mb.Color = Pal.Pear;
            Gem(mb, new Vector3(-0.28f, 1.06f, 0.24f), 0.06f);
            mb.Color = Pal.Brass;
            mb.Lathe(new[] { new Vector2(0.07f, 1.0f), new Vector2(0.06f, 1.08f), new Vector2(0.025f, 1.15f), new Vector2(0f, 1.17f) }, 7, false, true, false);
            float[] cx = { -0.68f, -0.56f, 0.56f, 0.68f };
            for (int i = 0; i < cx.Length; i++)
            {
                float h = 0.18f + (i % 2) * 0.1f;
                float z = i % 2 == 0 ? 0.0f : 0.15f;
                mb.Color = Pal.Cream;
                mb.Cylinder(new Vector3(cx[i], 1.0f, z), 0.045f, 0.04f, h, 6);
                mb.Emission = lit ? 1f : 0f;
                mb.Color = lit ? Pal.FireCore : Pal.Charcoal;
                if (lit)
                {
                    mb.Blade(new Vector3(cx[i], 1.0f + h, z), new Vector3(cx[i], 1.0f + h + 0.11f, z), 0.06f, Vector3.right);
                    mb.Blade(new Vector3(cx[i], 1.0f + h, z), new Vector3(cx[i], 1.0f + h + 0.11f, z), 0.06f, Vector3.forward);
                }
                else Gem(mb, new Vector3(cx[i], 1.0f + h + 0.01f, z), 0.012f);
                mb.Emission = 0f;
            }
            return mb;
        }
    }
}
