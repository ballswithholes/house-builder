// The backdrop of a map (World / MapView): what the 2.5D parallax layers (def.layers) painted, rebuilt as real 3D
// geometry behind and around the play strip so it parallaxes naturally with the perspective camera.
//
//   bg_mountains_far → two ridges of faceted, snow-capped peaks 230–330 m behind the strip (fog turns them bluish)
//   bg_hills_far     → the terrain's own rolling hills (MapTerrain: field patches, tree clumps)
//   bg_village_far   → clusters of tiny cottages on the hills (warm windows at night) and a turning windmill
//   bg_forest_far/near → tree lines (round crowns and firs) on the hills behind, denser and bigger when near
//   bg_shrine_cliffs → tall columns of grey-violet stone with a stair climbing to a vermilion gate, dark stone lanterns
//                      up the stair and on the ledges (they wake, lowest first, as the map's spirit lanterns are lit)
//   bg_clouds        → soft cloud banks drifting across the sky (scrollSpeed)
// A layer's tint tints its geometry. A map without layers gets its biome's defaults (DefaultLayers), and the new biomes
// add their own scenery (MapBackdrop.Biomes.cs): highlands → rolling golden downs, windmills and standing stones; fen →
// rows of dead trees and reed banks in the mist; peaks → snowy ridges and snow-laden pines; roost → the jagged crown
// of the summit over a sea of cloud. Indoors (a cave or crypt) there is no backdrop at all. Everything is built in Y-up model space under one World3D.Upright root
// (local x = world x, local y = height, local z = world depth y), merged into a few meshes.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.Game
{
    internal sealed partial class MapBackdrop
    {
        readonly MapDef def;
        readonly MapTerrain terrain;
        readonly Transform root;
        readonly List<Mesh> owned;
        readonly float W, D;
        readonly System.Random rng;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        // clouds
        const float CloudTile = 720f;
        Transform cloudA, cloudB;
        float cloudSpeed, cloudScroll, cloudBaseX;

        // night windows of the distant village
        Renderer windows;
        float windowGlow = -1f;
        readonly List<Transform> sails = new List<Transform>();

        // shrine cliff lanterns: groups lit lowest first
        const int LanternGroups = 8;
        readonly List<Renderer> lanternBoxes = new List<Renderer>();
        readonly List<Renderer> lanternHalos = new List<Renderer>();
        readonly float[] groupLevel = new float[LanternGroups];
        int groupsLit;
        bool lanternAnim = true;

        public bool HasForest { get; private set; }
        public bool HasCliffLanterns => lanternBoxes.Count > 0;

        public MapBackdrop(MapDef def, MapTerrain terrain, Transform parent, List<Mesh> owned)
        {
            this.def = def;
            this.terrain = terrain;
            this.owned = owned;
            W = Mathf.Max(1f, def.width);
            D = Mathf.Max(1f, def.depth);
            rng = new System.Random(MapTerrain.StableHash(def.id) * 13 + 5);
            root = new GameObject("Backdrop").transform;
            root.SetParent(parent, false);
            root.localRotation = World3D.Upright;
            foreach (var l in Layers())
                if (l != null && l.art != null && l.art.Contains("forest")) HasForest = true;
        }

        /// <summary>The map's layers, or (none authored) its biome's defaults.</summary>
        List<ParallaxLayerDef> Layers() => def.layers != null && def.layers.Count > 0 ? def.layers : DefaultLayers(def.biome);

        float R() => (float)rng.NextDouble();
        float Range(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Local (Y-up) point on the terrain at a ground point.</summary>
        Vector3 Ground(float x, float y, float lift = 0f) => new Vector3(x, terrain.Height(x, y) + lift, y);

        static Color Tinted(Color c, Color tint) => new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a);

        // ================================================================== build

        public void Build()
        {
            // indoors there is nothing beyond the walls but the dark of the vault
            if (DayNight.IsIndoor(def)) return;
            foreach (var l in Layers())
            {
                if (l == null || string.IsNullOrEmpty(l.art)) continue;
                string a = l.art;
                var tint = string.IsNullOrEmpty(l.tint) ? Color.white : Color.Lerp(Color.white, Ui.Hex(l.tint), 0.8f);
                if (a.Contains("cloud")) BuildClouds(tint, l.scrollSpeed);
                else if (a.Contains("mountain")) BuildMountains(tint);
                else if (a.Contains("village")) BuildVillage(tint);
                else if (a.Contains("forest_near")) BuildForest(true, tint);
                else if (a.Contains("forest")) BuildForest(false, tint);
                else if (a.Contains("cliff")) BuildCliffs(tint);
                // bg_hills_*: the terrain's own hills
            }
            BuildBiome();
        }

        GameObject Emit(MeshBuilder mb, string name, float fogScale, Material material = null)
        {
            if (mb.IsEmpty) return null;
            var m = mb.ToMesh("lv_bd_" + name.Replace(' ', '_').ToLowerInvariant());
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material != null ? material : Materials3D.LowPoly;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (Mathf.Abs(fogScale - 1f) > 0.001f)
            {
                block.Clear();
                block.SetFloat(Materials3D.FogScaleId, fogScale);
                r.SetPropertyBlock(block);
            }
            return go;
        }

        // ------------------------------------------------------------------ mountains

        void BuildMountains(Color tint)
        {
            var mb = new MeshBuilder(rng.Next()) { Jitter = 0.07f };
            var rock = Tinted(new Color(0.56f, 0.59f, 0.75f), tint);
            var rockFar = Tinted(new Color(0.62f, 0.65f, 0.82f), tint);
            var snow = Tinted(new Color(0.95f, 0.96f, 1f), Color.Lerp(Color.white, tint, 0.5f));
            float cx = W * 0.5f;
            Ridge(mb, cx, D + 300f, 52f, 95f, 175f, rockFar, snow, 0.6f);
            Ridge(mb, cx, D + 232f, 40f, 55f, 115f, rock, snow, 0.72f);
            Emit(mb, "Mountains", 0.62f);
        }

        void Ridge(MeshBuilder mb, float cx, float depth, float spacing, float hMin, float hMax, Color rock, Color snow, float snowLine)
        {
            float seed = Range(0f, 100f);
            for (float x = cx - 330f; x < cx + 330f; x += spacing * Range(0.7f, 1.3f))
            {
                float n = Mathf.PerlinNoise(x * 0.008f + seed, seed * 0.5f);
                float h = Mathf.Lerp(hMin, hMax, n) * Range(0.85f, 1.12f);
                float z = depth + Range(-22f, 22f);
                Peak(mb, new Vector3(x, -12f, z), h, h * Range(0.8f, 1.15f), rock, snow, snowLine);
                int shoulders = 1 + rng.Next(2);
                for (int s = 0; s < shoulders; s++)
                {
                    float side = R() < 0.5f ? -1f : 1f;
                    float sh = h * Range(0.45f, 0.7f);
                    Peak(mb, new Vector3(x + side * h * Range(0.45f, 0.8f), -12f, z + Range(-14f, 10f)), sh, sh * Range(0.9f, 1.3f), rock, snow, snowLine + 0.1f);
                }
            }
        }

        void Peak(MeshBuilder mb, Vector3 b, float h, float r, Color rock, Color snow, float snowLine)
        {
            float sl = Mathf.Clamp(snowLine + Range(-0.06f, 0.06f), 0.4f, 0.95f);
            var prof = new[] { new Vector2(r, 0f), new Vector2(r * (1f - sl), h * sl), new Vector2(r * (1f - sl - 0.05f), h * (sl + 0.05f)), new Vector2(0f, h) };
            var shade = Paint.Shade(rock, Range(0.9f, 1.08f));
            var cols = new[] { Paint.Shade(shade, 0.9f), shade, snow, snow };
            mb.Push().Translate(b).Rotate(0f, Range(0f, 360f), 0f).Scale(new Vector3(1f, 1f, Range(0.7f, 1.1f)));
            mb.Lathe(prof, 5 + rng.Next(3), false, false, false, cols, Range(0f, 60f));
            mb.Pop();
        }

        // ------------------------------------------------------------------ clouds

        void BuildClouds(Color tint, float scrollSpeed)
        {
            var mb = new MeshBuilder(rng.Next()) { Jitter = 0.04f };
            var top = Tinted(new Color(1f, 0.98f, 0.95f), tint);
            var under = Tinted(new Color(0.8f, 0.82f, 0.93f), tint);
            int clouds = 11;
            for (int i = 0; i < clouds; i++)
            {
                float x = (i + Range(0.15f, 0.85f)) * CloudTile / clouds;
                float z = D + Range(150f, 330f);
                float y = Mathf.Lerp(70f, 125f, Mathf.InverseLerp(D + 150f, D + 330f, z)) + Range(-8f, 8f);
                float size = Range(0.8f, 1.35f);
                int blobs = 4 + rng.Next(4);
                for (int b = 0; b < blobs; b++)
                {
                    float t = blobs > 1 ? (float)b / (blobs - 1) : 0.5f;
                    float bx = Mathf.Lerp(-34f, 34f, t) * size + Range(-6f, 6f);
                    float centre = 1f - Mathf.Abs(t - 0.5f) * 2f;
                    float rad = Mathf.Lerp(12f, 26f, centre) * size * Range(0.85f, 1.15f);
                    float by = rad * Range(0.15f, 0.4f) * centre;
                    mb.Color = Color.Lerp(under, top, 0.35f + 0.65f * centre);
                    mb.Blob(new Vector3(x + bx, y + by, z + Range(-8f, 8f)), new Vector3(rad, rad * Range(0.5f, 0.65f), rad * Range(0.6f, 0.85f)), 1, 0.1f, rng.Next(1000), 0.7f);
                }
            }
            var go = Emit(mb, "Clouds A", 0.3f);
            if (go == null) return;
            cloudA = go.transform;
            var b2 = new GameObject("Clouds B");
            b2.transform.SetParent(root, false);
            b2.AddComponent<MeshFilter>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            var r2 = b2.AddComponent<MeshRenderer>();
            var r1 = go.GetComponent<MeshRenderer>();
            r2.sharedMaterial = r1.sharedMaterial;
            r2.shadowCastingMode = ShadowCastingMode.Off;
            r2.receiveShadows = false;
            r2.lightProbeUsage = LightProbeUsage.Off;
            r2.reflectionProbeUsage = ReflectionProbeUsage.Off;
            block.Clear();
            block.SetFloat(Materials3D.FogScaleId, 0.3f);
            r2.SetPropertyBlock(block);
            cloudB = b2.transform;
            cloudSpeed = scrollSpeed;
            cloudBaseX = W * 0.5f - CloudTile * 0.5f;
            cloudScroll = Range(0f, CloudTile);
            PlaceClouds();
        }

        void PlaceClouds()
        {
            float off = Mathf.Repeat(cloudScroll, CloudTile);
            cloudA.localPosition = new Vector3(cloudBaseX + off, 0f, 0f);
            cloudB.localPosition = new Vector3(cloudBaseX + off - CloudTile, 0f, 0f);
        }

        // ------------------------------------------------------------------ distant village

        void BuildVillage(Color tint)
        {
            var mb = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            var win = new MeshBuilder(rng.Next()) { Emission = 1f, Color = new Color(1f, 0.8f, 0.46f) };
            var walls = new[] { Ui.Hex("#f2e6cf"), Ui.Hex("#efe1c4"), Ui.Hex("#e8dccb"), Ui.Hex("#f5ecdc") };
            var roofs = new[] { Ui.Hex("#c4654a"), Ui.Hex("#5b7fae"), Ui.Hex("#c9a86a"), Ui.Hex("#b5573f"), Ui.Hex("#6b8fb8") };
            var leaf = new[] { Ui.Hex("#7aa257"), Ui.Hex("#5f8c4c"), Ui.Hex("#8cb262") };
            float[] centres = { W * 0.18f + Range(-8f, 8f), W * 0.52f + Range(-8f, 8f), W * 0.86f + Range(-8f, 8f) };
            bool mill = false;
            foreach (float cx in centres)
            {
                float cz = D + Range(42f, 72f);
                int houses = 4 + rng.Next(4);
                for (int i = 0; i < houses; i++)
                {
                    float x = cx + Range(-16f, 16f), z = cz + Range(-9f, 9f);
                    if (terrain.Reserved(x, z, 3f)) continue;
                    var basePt = new Vector3(x, terrain.Height(x, z) - 0.4f, z);
                    House(mb, win, basePt, Range(-18f, 18f), Tinted(walls[rng.Next(walls.Length)], tint), Tinted(roofs[rng.Next(roofs.Length)], tint), Range(0.85f, 1.2f));
                }
                for (int i = 0; i < 6; i++)
                {
                    float x = cx + Range(-22f, 22f), z = cz + Range(-12f, 12f);
                    if (terrain.Reserved(x, z, 3f)) continue;
                    MapTerrain.RoundTree(mb, rng, new Vector3(x, terrain.Height(x, z) - 0.3f, z), Range(4f, 6.5f), Tinted(leaf[rng.Next(leaf.Length)], tint));
                }
                if (!mill)
                {
                    mill = true;
                    float x = cx + Range(18f, 26f), z = cz + Range(-4f, 6f);
                    Windmill(mb, new Vector3(x, terrain.Height(x, z) - 0.4f, z), tint);
                }
            }
            Emit(mb, "Village Far", 0.85f);
            var wgo = Emit(win, "Village Windows", 0.85f);
            if (wgo != null) windows = wgo.GetComponent<MeshRenderer>();
        }

        void House(MeshBuilder mb, MeshBuilder win, Vector3 b, float yaw, Color wall, Color roof, float s)
        {
            float w = 3.6f * s, d = 3.2f * s, h = 2.6f * s;
            mb.Push().Translate(b).Rotate(0f, yaw, 0f);
            win.Push().Translate(b).Rotate(0f, yaw, 0f);
            mb.Color = wall;
            mb.BoxOn(Vector3.zero, new Vector3(w, h + 0.4f, d));
            mb.Color = Paint.Shade(roof, Range(0.92f, 1.06f));
            mb.Roof(new Vector3(0f, h + 0.4f, 0f), w, d, 1.9f * s, 0.3f, 0.12f, true);
            mb.Color = Ui.Hex("#9a8576");
            mb.BoxOn(new Vector3(w * 0.28f, h + 0.9f, d * 0.15f), new Vector3(0.4f, 1.6f * s, 0.4f));
            mb.Color = Ui.Hex("#5a4234");
            mb.BoxOn(new Vector3(-w * 0.18f, 0.4f, -d * 0.5f - 0.03f), new Vector3(0.7f, 1.4f, 0.08f));
            win.BoxOn(new Vector3(w * 0.22f, 1.5f, -d * 0.5f - 0.04f), new Vector3(0.6f, 0.6f, 0.08f));
            if (R() < 0.6f) win.BoxOn(new Vector3(-w * 0.5f - 0.04f, 1.6f, 0f), new Vector3(0.08f, 0.55f, 0.6f));
            mb.Pop();
            win.Pop();
        }

        void Windmill(MeshBuilder mb, Vector3 b, Color tint)
        {
            mb.Color = Tinted(Ui.Hex("#efe3cc"), tint);
            mb.Push().Translate(b);
            mb.Lathe(new[] { new Vector2(2.2f, 0f), new Vector2(1.5f, 7.5f) }, 8, false, false, true);
            mb.Color = Tinted(Ui.Hex("#b5573f"), tint);
            mb.Cone(new Vector3(0f, 7.4f, 0f), 1.9f, 2.2f, 8);
            mb.Pop();
            var smb = new MeshBuilder(rng.Next()) { Jitter = 0.04f, Color = Tinted(Ui.Hex("#e9dcc0"), tint) };
            for (int i = 0; i < 4; i++)
            {
                smb.Push().Rotate(0f, 0f, i * 90f);
                smb.Box(new Vector3(0f, 3.4f, 0f), new Vector3(1.2f, 5.6f, 0.08f));
                smb.Pop();
            }
            smb.Color = Ui.Hex("#6b5040");
            smb.Box(Vector3.zero, new Vector3(0.6f, 0.6f, 0.4f));
            var go = Emit(smb, "Village Windmill Sails", 0.85f);
            if (go == null) return;
            go.transform.localPosition = b + new Vector3(0f, 7.6f, -1.9f);
            sails.Add(go.transform);
        }

        // ------------------------------------------------------------------ forests

        void BuildForest(bool near, Color tint)
        {
            // the biome's woods (Biomes: green for the original maps, golden, fen-dark or snowy-pine for the new ones)
            var biome = Biomes.For(def);
            var leafNear = biome.BackdropLeafNear;
            var leafFar = biome.BackdropLeafFar;
            var pal = near ? leafNear : leafFar;
            float[] rows = near ? new[] { 8.5f, 11f, 14f, 17.5f, 22f } : new[] { 34f, 42f, 52f, 64f, 78f };
            float span = near ? 75f : 165f;
            float step = near ? 2.9f : 5f;
            const float chunk = 60f;
            for (float c0 = -span; c0 < W + span; c0 += chunk)
            {
                var mb = new MeshBuilder(rng.Next()) { Jitter = 0.07f };
                for (int ri = 0; ri < rows.Length; ri++)
                {
                    float z0 = D + rows[ri];
                    for (float x = c0 + Range(0f, step); x < Mathf.Min(W + span, c0 + chunk); x += step * Range(0.75f, 1.25f))
                    {
                        float z = z0 + Range(-1.6f, 1.6f);
                        if (terrain.Reserved(x, z, 1.5f)) continue;
                        Tree(mb, x, z, near, ri, pal, tint);
                    }
                }
                if (near)
                {
                    // the wood closes in at the sides too (clear of the roads out of the map and the camera's side)
                    for (float x = c0 + Range(0f, 4f); x < Mathf.Min(W + span, c0 + chunk); x += Range(3.5f, 5.5f))
                    {
                        if (x > -11f && x < W + 11f) continue;
                        for (float z = -34f; z < D + 7f; z += Range(3.5f, 5.5f))
                        {
                            float dOut = terrain.DistanceOutside(x, z);
                            if (z < -6f && dOut < 18f) continue;
                            if (terrain.Reserved(x, z, 3f)) continue;
                            Tree(mb, x + Range(-1f, 1f), z, true, 2, pal, tint);
                        }
                    }
                }
                Emit(mb, (near ? "Forest Near " : "Forest Far ") + c0, near ? 1f : 0.85f);
            }
        }

        void Tree(MeshBuilder mb, float x, float z, bool near, int row, Color[] pal, Color tint)
        {
            var b = Ground(x, z, -0.4f);
            var leaf = Tinted(Paint.Shade(pal[rng.Next(pal.Length)], Range(0.88f, 1.1f)), tint);
            float s = near ? Range(0.85f, 1.3f) : Range(0.8f, 1.2f);
            int detail = near && row < 3 ? 1 : 0;
            if (R() < 0.42f)
            {
                // fir
                float h = (near ? 13f : 11f) * s;
                mb.Wind = 0f; mb.WindGradient = false;
                mb.Color = Tinted(Ui.Hex("#5a4232"), tint);
                mb.Cylinder(b, h * 0.035f, h * 0.02f, h * 0.3f, 5);
                if (near) { mb.Wind = 0.18f; mb.WindGradient = true; mb.WindY0 = b.y + h * 0.25f; mb.WindY1 = b.y + h; }
                int tiers = near ? 4 : 3;
                for (int i = 0; i < tiers; i++)
                {
                    float y0 = h * (0.18f + i * 0.19f);
                    float rr = h * 0.22f * (1f - i * 0.2f);
                    mb.Color = Paint.Shade(leaf, 0.85f + 0.07f * i);
                    mb.Cylinder(b + new Vector3(0f, y0, 0f), rr, 0f, h * (0.4f - i * 0.04f), near ? 7 : 5, false, true, false);
                }
            }
            else
            {
                // broadleaf: trunk and two or three crowns
                float h = (near ? 11f : 9f) * s;
                mb.Wind = 0f; mb.WindGradient = false;
                mb.Color = Tinted(Ui.Hex("#5e4a3a"), tint);
                float th = h * 0.5f;
                mb.Cylinder(b, h * 0.05f, h * 0.035f, th + 0.5f, 6);
                if (near) { mb.Wind = 0.22f; mb.WindGradient = true; mb.WindY0 = b.y + th; mb.WindY1 = b.y + h; }
                float cr = h * 0.3f;
                int crowns = 2 + rng.Next(2);
                for (int i = 0; i < crowns; i++)
                {
                    mb.Color = Paint.Shade(leaf, Range(0.9f, 1.12f));
                    var c = b + new Vector3(Range(-cr, cr) * 0.6f, th + cr * Range(0.6f, 1.3f), Range(-cr, cr) * 0.4f);
                    float r = cr * Range(0.75f, 1.05f);
                    mb.Blob(c, new Vector3(r, r * 0.85f, r), detail, 0.15f, rng.Next(1000), 0.3f);
                }
            }
            mb.Wind = 0f; mb.WindGradient = false;
        }

        // ------------------------------------------------------------------ shrine cliffs

        void BuildCliffs(Color tint)
        {
            var mb = new MeshBuilder(rng.Next()) { Jitter = 0.06f };
            var rockLow = Tinted(Ui.Hex("#7d7790"), tint);
            var rockHigh = Tinted(Ui.Hex("#a9a3ba"), tint);
            var moss = Tinted(Ui.Hex("#7f9a6a"), tint);
            float yc = D + 15f;
            float stairX = W * 0.55f;
            float fallX = stairX - 31f;
            const float top = 27f;
            var ledges = new List<Vector3>();

            // columns: two rows of tall hexagonal stone pillars, a gap behind the stair where a sheer face stands and a
            // narrow cleft for the waterfall
            for (int row = 0; row < 2; row++)
            {
                for (float x = -70f + Range(0f, 3f); x < W + 70f; x += Range(3.3f, 4.6f))
                {
                    if (row == 0 && Mathf.Abs(x - stairX) < 13f) continue;
                    if (row == 0 && Mathf.Abs(x - fallX) < 2.6f) continue;
                    float z = yc + (row == 0 ? Range(0f, 2f) : Range(5f, 8f));
                    if (terrain.Reserved(x, z, 2f)) continue;
                    float n = Mathf.PerlinNoise(x * 0.05f + 3.7f, row * 1.3f);
                    float h = (row == 0 ? Mathf.Lerp(18f, 29f, n) : Mathf.Lerp(24f, 38f, n)) * Range(0.9f, 1.1f);
                    if (R() < 0.08f) h *= 1.25f;
                    float r = (row == 0 ? Range(2.2f, 3.3f) : Range(2.6f, 4f));
                    float gy = terrain.Height(x, z) - 4f;
                    var prof = new[] { new Vector2(r, 0f), new Vector2(r * 0.97f, h * 0.55f), new Vector2(r * 0.92f, h * 0.97f), new Vector2(r * 0.88f, h) };
                    var cols = new[] { Paint.Shade(rockLow, 0.85f), Color.Lerp(rockLow, rockHigh, 0.5f), rockHigh, moss };
                    mb.Push().Translate(x, gy, z);
                    mb.Lathe(prof, 6, false, false, true, cols, Range(0f, 60f));
                    mb.Pop();
                    float roll = R();
                    if (roll < 0.3f) MapTerrain.Pine(mb, rng, new Vector3(x + Range(-1f, 1f), gy + h, z + Range(-0.5f, 1f)), Range(3f, 5.5f), Tinted(Ui.Hex("#4f6f55"), tint));
                    else if (row == 0 && roll < 0.5f) ledges.Add(new Vector3(x + Range(-0.6f, 0.6f), gy + h, z - r * 0.45f));
                }
            }

            // the sheer face and its stair (zig-zag flights with landings)
            float faceZ = yc + 1.5f;
            float g0 = terrain.Height(stairX, yc - 1.5f);
            mb.Color = Color.Lerp(rockLow, rockHigh, 0.4f);
            mb.Box(new Vector3(stairX, g0 + top * 0.5f - 3f, faceZ + 2f), new Vector3(27f, top + 6f, 5f), moss);
            var stone = Tinted(Ui.Hex("#b9b2c4"), tint);
            var lanterns = new List<Vector3>();
            const int flights = 4;
            const float run = 10f, rise = top / flights;
            const int steps = 22;
            float sx = stairX - run * 0.5f, sy = g0;
            for (int f = 0; f < flights; f++)
            {
                float dir = (f & 1) == 0 ? 1f : -1f;
                for (int s = 0; s < steps; s++)
                {
                    float t = (s + 0.5f) / steps;
                    var p = new Vector3(sx + dir * run * t, sy + rise * t, faceZ - 1.2f);
                    mb.Color = Paint.Shade(stone, Range(0.92f, 1.05f));
                    mb.BoxOn(p - new Vector3(0f, 0.6f, 0f), new Vector3(run / steps + 0.06f, 0.6f + 0.02f, 1.6f));
                    if (s % 5 == 2) lanterns.Add(new Vector3(p.x, p.y, faceZ - 1.75f));
                }
                sx += dir * run;
                sy += rise;
                mb.Color = stone;
                mb.BoxOn(new Vector3(sx, sy - 0.5f, faceZ - 1.2f), new Vector3(2.2f, 0.5f, 1.8f));
                lanterns.Add(new Vector3(sx + dir * 0.7f, sy, faceZ - 1.7f));
            }
            // vermilion gate at the top of the stair
            ShrineGate(mb, new Vector3(sx, sy, faceZ - 1.2f), tint);
            // a thin waterfall down a gap in the columns
            float wx = fallX;
            float wg = terrain.Height(wx, yc);
            var fall = new MeshBuilder(rng.Next()) { Emission = 0.3f, Color = Tinted(new Color(0.86f, 0.92f, 1f), tint), Jitter = 0.05f };
            fall.Box(new Vector3(wx, wg + top * 0.5f, yc - 0.5f), new Vector3(1.3f, top + 2f, 0.4f));
            fall.Blob(new Vector3(wx, wg + 0.6f, yc - 1.5f), new Vector3(2.6f, 1.2f, 1.8f), 1, 0.2f, 7, 0.4f);
            Emit(fall, "Waterfall", 0.85f);

            // dark stone lanterns: up the stair, at the cliff foot, on a few ledges
            for (float x = -40f; x < W + 40f; x += Range(9f, 14f))
            {
                float z = yc - Range(4f, 5.5f);
                if (Mathf.Abs(x - stairX) < 14f || terrain.Reserved(x, z, 2f)) continue;
                lanterns.Add(new Vector3(x, terrain.Height(x, z), z));
            }
            // on the column tops nearest the strip (where the stair of lanterns seems to continue into the sky)
            for (int i = 0; i < ledges.Count; i++)
                if (ledges[i].x > -45f && ledges[i].x < W + 45f) lanterns.Add(ledges[i]);
            Emit(mb, "Shrine Cliffs", 0.85f);
            BuildCliffLanterns(lanterns, stone, tint);
        }

        void ShrineGate(MeshBuilder mb, Vector3 b, Color tint)
        {
            var red = Tinted(Ui.Hex("#c8442e"), tint);
            var black = Ui.Hex("#2b2326");
            for (int s = -1; s <= 1; s += 2)
            {
                mb.Color = black;
                mb.Cylinder(b + new Vector3(s * 1.5f, 0f, 0f), 0.2f, 0.2f, 0.5f, 8);
                mb.Color = red;
                mb.Cylinder(b + new Vector3(s * 1.5f, 0.5f, 0f), 0.17f, 0.15f, 3.4f, 8);
            }
            mb.Color = red;
            mb.Box(b + new Vector3(0f, 3.2f, 0f), new Vector3(3.8f, 0.24f, 0.3f));
            mb.Color = black;
            mb.Box(b + new Vector3(0f, 3.75f, 0f), new Vector3(4.6f, 0.26f, 0.42f));
        }

        void BuildCliffLanterns(List<Vector3> spots, Color stone, Color tint)
        {
            spots.Sort((a, b) => a.y.CompareTo(b.y));
            int per = Mathf.Max(1, Mathf.CeilToInt(spots.Count / (float)LanternGroups));
            var body = new MeshBuilder(rng.Next()) { Jitter = 0.06f, Color = stone };
            var paper = new Color(1f, 0.8f, 0.46f);
            for (int g = 0; g < LanternGroups; g++)
            {
                var box = new MeshBuilder(rng.Next()) { Emission = 1f, Color = paper };
                var halo = new List<Vector3>();
                for (int i = g * per; i < Mathf.Min(spots.Count, (g + 1) * per); i++)
                {
                    var p = spots[i];
                    const float s = 1.35f;
                    body.Push().Translate(p).Scale(s);
                    body.Color = Paint.Shade(stone, Range(0.85f, 1.0f));
                    body.BoxOn(Vector3.zero, new Vector3(0.55f, 0.16f, 0.55f));
                    body.BoxOn(new Vector3(0f, 0.16f, 0f), new Vector3(0.2f, 0.62f, 0.2f));
                    body.BoxOn(new Vector3(0f, 0.78f, 0f), new Vector3(0.5f, 0.08f, 0.5f));
                    body.TaperedBox(new Vector3(0f, 1.24f, 0f), new Vector3(0.66f, 0.26f, 0.66f), 0.85f);
                    body.Pop();
                    box.Push().Translate(p).Scale(s);
                    box.BoxOn(new Vector3(0f, 0.86f, 0f), new Vector3(0.36f, 0.38f, 0.36f));
                    box.Pop();
                    halo.Add(p + new Vector3(0f, 1.05f * s, -0.4f));
                }
                var go = Emit(box, "Cliff Lanterns " + g, 0.85f);
                if (go == null) continue;
                lanternBoxes.Add(go.GetComponent<MeshRenderer>());
                lanternHalos.Add(HaloMesh(halo, "Cliff Lantern Glow " + g));
            }
            Emit(body, "Cliff Lantern Stones", 0.85f);
            ApplyLanterns();
        }

        /// <summary>Static additive glow quads (facing the default view, −Z in model space) around lantern fire boxes.</summary>
        Renderer HaloMesh(List<Vector3> centres, string name)
        {
            var verts = new List<Vector3>(centres.Count * 4);
            var uvs = new List<Vector2>(centres.Count * 4);
            var cols = new List<Color32>(centres.Count * 4);
            var tris = new List<int>(centres.Count * 6);
            const float h = 1.1f;
            foreach (var c in centres)
            {
                int b = verts.Count;
                verts.Add(c + new Vector3(-h, -h, 0f)); verts.Add(c + new Vector3(-h, h, 0f));
                verts.Add(c + new Vector3(h, h, 0f)); verts.Add(c + new Vector3(h, -h, 0f));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                var col = new Color32(255, 214, 140, 255);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var m = new Mesh { name = "lv_bd_" + name.Replace(' ', '_').ToLowerInvariant() };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            owned.Add(m);
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Materials3D.AdditiveFor(WorldTextures.Glow);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            return r;
        }

        /// <summary>Lights the cliff lanterns for a fraction (0..1) of the map's spirit lanterns lit, lowest first.</summary>
        public void SetLanternFraction(float fraction, bool animate)
        {
            if (lanternBoxes.Count == 0) return;
            groupsLit = Mathf.Clamp(Mathf.RoundToInt(fraction * lanternBoxes.Count), 0, lanternBoxes.Count);
            if (!animate)
            {
                for (int g = 0; g < lanternBoxes.Count; g++) groupLevel[g] = g < groupsLit ? 1f : 0f;
                ApplyLanterns();
            }
            lanternAnim = true;
        }

        void ApplyLanterns()
        {
            for (int g = 0; g < lanternBoxes.Count; g++)
            {
                float k = groupLevel[g];
                block.Clear();
                block.SetColor(Materials3D.TintId, Color.Lerp(new Color(0.26f, 0.24f, 0.32f), Color.white, k));
                block.SetFloat(Materials3D.FogScaleId, 0.85f);
                lanternBoxes[g].SetPropertyBlock(block);
                var h = lanternHalos[g];
                bool on = k > 0.01f;
                if (h.enabled != on) h.enabled = on;
                if (on)
                {
                    block.Clear();
                    block.SetColor(Materials3D.ColorId, new Color(1f, 0.85f, 0.6f, 0.55f * k));
                    block.SetFloat(Materials3D.FogScaleId, 0.5f);
                    h.SetPropertyBlock(block);
                }
            }
        }

        // ================================================================== per frame

        public void Update(float dt, float time, DayNight dn)
        {
            if (cloudA != null && Mathf.Abs(cloudSpeed) > 1e-5f)
            {
                cloudScroll += cloudSpeed * 18f * dt;
                PlaceClouds();
            }
            for (int i = 0; i < sails.Count; i++) sails[i].localRotation = Quaternion.Euler(0f, 0f, time * (14f - 2.5f * (i % 3)) + i * 37f);
            if (windows != null && Mathf.Abs(dn.NightGlow - windowGlow) > 0.01f)
            {
                windowGlow = dn.NightGlow;
                block.Clear();
                block.SetColor(Materials3D.TintId, Color.Lerp(new Color(0.3f, 0.28f, 0.34f), Color.white, Mathf.Clamp01(windowGlow * 1.2f)));
                block.SetFloat(Materials3D.FogScaleId, 0.85f);
                windows.SetPropertyBlock(block);
            }
            if (lanternAnim && lanternBoxes.Count > 0)
            {
                bool moving = false;
                for (int g = 0; g < lanternBoxes.Count; g++)
                {
                    // the lit groups wake one after another, lowest first (a stair of waking stars)
                    float target = g < groupsLit ? 1f : 0f;
                    float rate = 0.9f;
                    if (target > groupLevel[g] && g > 0 && groupLevel[g - 1] < 0.5f) continue;
                    float v = Mathf.MoveTowards(groupLevel[g], target, dt * rate);
                    if (Mathf.Abs(v - groupLevel[g]) > 1e-5f) moving = true;
                    groupLevel[g] = v;
                }
                // breathing glow once lit
                if (moving) ApplyLanterns();
                else
                {
                    lanternAnim = false;
                    ApplyLanterns();
                }
            }
        }
    }
}
