// The biome style table (World): how a map's land looks — ground and surroundings textures and tints, the terrain's
// relief and walls, its detail layer, ground cover and hill-tree palettes, the backdrop forest's leaves and the Map
// panel's base colour. MapTerrain, MapView (terrain material), MapBackdrop and the preview tool read it instead of
// sniffing keywords. A map picks its biome with MapDef.biome; maps without one fall back to keywords in their ground
// key (ground_forest → forest, ground_shrine → shrine, ground_village → village, otherwise meadow), which reproduces
// the three original maps exactly.
//
// Ground texture: a map whose biome brings its own ground (highlands → ground_highlands, …) uses it while MapDef.ground
// is still one of the four original placeholders (ground_meadow / forest / shrine / village); then its groundTint (a
// tint for the placeholder) is ignored too. Any other ground key in data wins.
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>The terrain's detail layer (Lanternvale/Terrain _DetailTex).</summary>
    internal enum BiomeDetail { None, Gravel, LeafLitter, Scree, Puddles, SnowDust, Roots, Ash }

    /// <summary>What grows (or lies) on the ground around and, with MapDef.fill, inside the map.</summary>
    internal enum BiomeCover { Meadow, Golden, Fen, Snow, Rubble, Ice, Crypt, Hollow, Ash }

    /// <summary>How the land rises around the map.</summary>
    internal enum BiomeRelief { Hills, Rolling, Flat, Mountains, Crags, Walls }

    /// <summary>Walls of an indoor map.</summary>
    internal enum BiomeWalls { None, Rock, Ice, Masonry, Roots }

    internal sealed class BiomeStyle
    {
        public string Id;
        /// <summary>The biome's own ground texture (null: the map's ground key as given).</summary>
        public string Ground;
        /// <summary>The texture of the land around the map.</summary>
        public string Side = "ground_meadow";
        public float SidePlanarScale = 1f / 8f;
        /// <summary>The map ground's tint factor (× groundTint): a little under white keeps it as painted.</summary>
        public float Inner = 0.86f;
        /// <summary>A colour cast over the whole land (new biomes only: the peaks' cold light on the snow).</summary>
        public Color Tint = Color.white;
        public Color SideTint = new Color(0.94f, 0.98f, 0.88f);
        public float BlendStart = 1.5f, BlendEnd = 7.5f;
        public bool Forest, Paved, MeadowInside, Fields;
        public Color MeadowTint = new Color(0.9f, 0.95f, 0.84f);
        public Color MossTint = Color.white, GravelTint = Color.white;
        public BiomeDetail Detail;
        public float DetailTile = 4f;
        public BiomeRelief Relief = BiomeRelief.Hills;
        /// <summary>Hills × this (Hills relief: 1, the original maps).</summary>
        public float HillScale = 1f;
        public BiomeWalls Walls;
        /// <summary>Wall rock colours: at the foot, high up.</summary>
        public Color WallLow, WallHigh;
        /// <summary>Steep slopes (outdoors) turn to this rock colour (alpha = how much).</summary>
        public Color Rock = new Color(0f, 0f, 0f, 0f);
        /// <summary>A flat water table in the surroundings (fen pools between hummocks).</summary>
        public bool WaterTable;
        public BiomeCover Cover;
        public Color Grass = Ui.Hex("#7fa35a"), GrassDark = Ui.Hex("#5e8445"), Bush = Ui.Hex("#6e9a4e");
        public Color[] Flowers = { Ui.Hex("#fff6e6"), Ui.Hex("#ffd86b"), Ui.Hex("#f2a7c3"), Ui.Hex("#b9a6f0"), Ui.Hex("#ffb38a") };
        public bool HillTrees = true;
        public Color[] HillLeaf = { Ui.Hex("#6f9a52"), Ui.Hex("#5d8c4c"), Ui.Hex("#83a85a"), Ui.Hex("#4f7b4a") };
        public float PineShare = 0.3f;
        /// <summary>Snow on the hill pines' tiers.</summary>
        public bool SnowyPines;
        public Color[] BackdropLeafNear = { Ui.Hex("#3f6b45"), Ui.Hex("#4c7a4a"), Ui.Hex("#365f40"), Ui.Hex("#5a8650") };
        public Color[] BackdropLeafFar = { Ui.Hex("#5d8770"), Ui.Hex("#6b9478"), Ui.Hex("#557f6c") };
        /// <summary>The Map panel's base colour.</summary>
        public Color MapColor = Ui.Hex("#b8d48f");
        /// <summary>Indoors (cave, ice cave, crypt, the Hollow Heart): walled, no hills, trees or grass.</summary>
        public bool Indoor => Relief == BiomeRelief.Walls;

        public BiomeStyle Clone() => (BiomeStyle)MemberwiseClone();
    }

    internal static class Biomes
    {
        public const string Meadow = "meadow", Village = "village", Forest = "forest", Shrine = "shrine", Highlands = "highlands",
            Fen = "fen", Peaks = "peaks", Cave = "cave", IceCave = "ice_cave", Crypt = "crypt", HollowHeart = "hollow_heart", Roost = "roost";

        static Dictionary<string, BiomeStyle> table;

        /// <summary>The map's biome id: MapDef.biome, else the keyword fallback from its ground key.</summary>
        public static string IdOf(MapDef def)
        {
            if (def == null) return Meadow;
            if (!string.IsNullOrEmpty(def.biome) && Table.ContainsKey(def.biome)) return def.biome;
            string g = string.IsNullOrEmpty(def.ground) ? "" : def.ground;
            // the original maps' order: forest, then shrine, then village (meadow with a worn-in village ground)
            if (g.Contains("forest")) return Forest;
            if (g.Contains("shrine")) return Shrine;
            if (g.Contains("village")) return Village;
            if (g.Contains("highland")) return Highlands;
            if (g.Contains("fen")) return Fen;
            if (g.Contains("snow")) return Peaks;
            if (g.Contains("ice")) return IceCave;
            if (g.Contains("crypt")) return Crypt;
            if (g.Contains("cave")) return Cave;
            if (g.Contains("hollow")) return HollowHeart;
            if (g.Contains("roost")) return Roost;
            return Meadow;
        }

        public static BiomeStyle For(MapDef def) => Table[IdOf(def)];

        public static BiomeStyle ById(string id) => id != null && Table.TryGetValue(id, out var s) ? s : Table[Meadow];

        /// <summary>Indoors: an indoor environment, or (environment unset) an indoor biome.</summary>
        public static bool IsIndoor(MapDef def)
        {
            if (def == null) return false;
            string env = def.environment ?? "";
            if (env == "cave" || env == "crypt") return true;
            if (env == "outdoor") return false;
            return For(def).Indoor;
        }

        static bool Placeholder(string g) =>
            string.IsNullOrEmpty(g) || g == "ground_meadow" || g == "ground_forest" || g == "ground_shrine" || g == "ground_village";

        /// <summary>The map ground's texture key (see the header: a biome's own ground replaces a placeholder).</summary>
        public static string GroundKey(MapDef def)
        {
            var st = For(def);
            string g = def != null ? def.ground : null;
            if (st.Ground != null && Placeholder(g)) return st.Ground;
            return string.IsNullOrEmpty(g) ? "ground_meadow" : g;
        }

        /// <summary>The map's groundTint, unless the biome's own ground replaced the placeholder it was meant for.</summary>
        public static string GroundTint(MapDef def)
        {
            if (def == null) return "";
            var st = For(def);
            if (st.Ground != null && Placeholder(def.ground)) return "";
            return def.groundTint ?? "";
        }

        /// <summary>The surroundings' texture key.</summary>
        public static string SideKey(MapDef def) => For(def).Side;

        /// <summary>The surroundings' planar scale (1 / metres per tile).</summary>
        public static float SidePlanarScale(MapDef def) => For(def).SidePlanarScale;

        /// <summary>A ground texture's average colour when it can't be measured (MapView.Average).</summary>
        public static Color AverageFallback(string key)
        {
            key = key ?? "";
            if (key.Contains("forest")) return new Color(0.36f, 0.38f, 0.25f);
            if (key.Contains("shrine")) return new Color(0.66f, 0.65f, 0.6f);
            if (key.Contains("village")) return new Color(0.74f, 0.66f, 0.5f);
            if (key.Contains("highland")) return new Color(0.73f, 0.66f, 0.41f);
            if (key.Contains("fen")) return new Color(0.43f, 0.45f, 0.33f);
            if (key.Contains("snow")) return new Color(0.86f, 0.87f, 0.89f);
            if (key.Contains("ice")) return new Color(0.76f, 0.82f, 0.87f);
            if (key.Contains("crypt")) return new Color(0.52f, 0.51f, 0.53f);
            if (key.Contains("cave")) return new Color(0.54f, 0.5f, 0.47f);
            if (key.Contains("hollow")) return new Color(0.33f, 0.29f, 0.32f);
            if (key.Contains("roost")) return new Color(0.53f, 0.5f, 0.49f);
            return new Color(0.6f, 0.69f, 0.45f);
        }

        static Dictionary<string, BiomeStyle> Table => table ?? (table = Build());

        static Dictionary<string, BiomeStyle> Build()
        {
            var t = new Dictionary<string, BiomeStyle>(System.StringComparer.Ordinal);

            // ---------------------------------------------------------------- the original looks (exactly as before)
            var meadow = new BiomeStyle { Id = Meadow };
            t[Meadow] = meadow;

            var village = meadow.Clone();
            village.Id = Village;
            village.MeadowInside = true;
            village.MapColor = Ui.Hex("#d9c79c");
            t[Village] = village;

            t[Forest] = new BiomeStyle
            {
                Id = Forest, Forest = true, Inner = 0.93f, SideTint = new Color(0.76f, 0.88f, 0.68f), BlendStart = 4f, BlendEnd = 15f,
                Detail = BiomeDetail.LeafLitter, DetailTile = 3.2f,
                Grass = Ui.Hex("#5f8a4c"), GrassDark = Ui.Hex("#456f3d"), Bush = Ui.Hex("#5f8f4a"), MapColor = Ui.Hex("#9fbf80"),
            };
            t[Shrine] = new BiomeStyle
            {
                Id = Shrine, Paved = true, Inner = 0.9f, MossTint = new Color(0.66f, 0.86f, 0.56f), GravelTint = new Color(0.94f, 0.92f, 0.82f),
                SideTint = new Color(0.7f, 0.86f, 0.62f), BlendStart = 1.2f, BlendEnd = 6f, Detail = BiomeDetail.Gravel, DetailTile = 2.4f,
                Grass = Ui.Hex("#86a070"), GrassDark = Ui.Hex("#5f7a5c"), Bush = Ui.Hex("#6a9256"), MapColor = Ui.Hex("#c9c1b0"),
            };

            // ---------------------------------------------------------------- outdoors, levels 12-30
            t[Highlands] = new BiomeStyle
            {
                Id = Highlands, Ground = "ground_highlands", Side = "ground_highlands", SideTint = new Color(1f, 0.97f, 0.86f),
                Inner = 0.88f, BlendStart = 2f, BlendEnd = 10f, Fields = true, Relief = BiomeRelief.Rolling, HillScale = 0.8f,
                Cover = BiomeCover.Golden,
                Grass = Ui.Hex("#d8bf66"), GrassDark = Ui.Hex("#8c8442"), Bush = Ui.Hex("#7e9a4c"),
                Flowers = new[] { Ui.Hex("#e0503e"), Ui.Hex("#e0503e"), Ui.Hex("#6f8fe0"), Ui.Hex("#fff6e6"), Ui.Hex("#ffd86b") },
                HillLeaf = new[] { Ui.Hex("#8fa552"), Ui.Hex("#a3a957"), Ui.Hex("#7c9a4c"), Ui.Hex("#c9a24a") }, PineShare = 0.12f,
                BackdropLeafNear = new[] { Ui.Hex("#6f8a45"), Ui.Hex("#8a9a4c"), Ui.Hex("#5f7d43"), Ui.Hex("#a8964a") },
                BackdropLeafFar = new[] { Ui.Hex("#8a9a6a"), Ui.Hex("#9ca472"), Ui.Hex("#7d9068") },
                MapColor = Ui.Hex("#d8c98a"),
            };
            t[Fen] = new BiomeStyle
            {
                Id = Fen, Ground = "ground_fen", Side = "ground_fen", SideTint = new Color(0.86f, 0.9f, 0.8f), Inner = 0.9f,
                BlendStart = 3f, BlendEnd = 12f, Relief = BiomeRelief.Flat, HillScale = 0.32f, WaterTable = true,
                Detail = BiomeDetail.Puddles, DetailTile = 5f, Cover = BiomeCover.Fen,
                Grass = Ui.Hex("#8a9356"), GrassDark = Ui.Hex("#5b6b40"), Bush = Ui.Hex("#61744a"),
                Flowers = new[] { Ui.Hex("#f4f1de"), Ui.Hex("#f4f1de"), Ui.Hex("#e8cf5a"), Ui.Hex("#c9b6e8") },
                HillLeaf = new[] { Ui.Hex("#6c7d4e"), Ui.Hex("#5a6d47"), Ui.Hex("#7a8a55"), Ui.Hex("#4f6145") }, PineShare = 0.05f,
                BackdropLeafNear = new[] { Ui.Hex("#4c5f45"), Ui.Hex("#56694a"), Ui.Hex("#43553f"), Ui.Hex("#61704c") },
                BackdropLeafFar = new[] { Ui.Hex("#6d7d6c"), Ui.Hex("#768573"), Ui.Hex("#647465") },
                MapColor = Ui.Hex("#9aa47c"),
            };
            t[Peaks] = new BiomeStyle
            {
                Id = Peaks, Ground = "ground_snow", Side = "ground_snow", SideTint = new Color(0.93f, 0.96f, 1f), Inner = 0.9f, Tint = new Color(0.94f, 0.98f, 1.06f),
                BlendStart = 2f, BlendEnd = 9f, Relief = BiomeRelief.Mountains, HillScale = 1.55f,
                Rock = new Color(0.52f, 0.55f, 0.62f, 0.9f), Cover = BiomeCover.Snow,
                Grass = Ui.Hex("#a69a72"), GrassDark = Ui.Hex("#7d7458"), Bush = Ui.Hex("#5f7a6c"),
                Flowers = new[] { Ui.Hex("#eef2ff"), Ui.Hex("#b9c8f0") },
                HillLeaf = new[] { Ui.Hex("#3f5f55"), Ui.Hex("#4a6a5c"), Ui.Hex("#365449"), Ui.Hex("#557263") }, PineShare = 0.9f, SnowyPines = true,
                BackdropLeafNear = new[] { Ui.Hex("#3b574f"), Ui.Hex("#45625a"), Ui.Hex("#334d46"), Ui.Hex("#4f6b62") },
                BackdropLeafFar = new[] { Ui.Hex("#6d8590"), Ui.Hex("#768e98"), Ui.Hex("#647b86") },
                MapColor = Ui.Hex("#e4ebf2"),
            };
            t[Roost] = new BiomeStyle
            {
                Id = Roost, Ground = "ground_roost", Side = "ground_roost", SideTint = new Color(0.86f, 0.84f, 0.84f), Inner = 0.88f,
                BlendStart = 1.5f, BlendEnd = 8f, Relief = BiomeRelief.Crags, HillScale = 1.3f,
                Rock = new Color(0.44f, 0.41f, 0.41f, 0.75f), Cover = BiomeCover.Ash,
                Grass = Ui.Hex("#8f897c"), GrassDark = Ui.Hex("#625c55"), Bush = Ui.Hex("#5d5a50"),
                Flowers = new[] { Ui.Hex("#d9a066") }, HillTrees = false,
                MapColor = Ui.Hex("#a49c98"),
            };

            // ---------------------------------------------------------------- indoors
            t[Cave] = new BiomeStyle
            {
                Id = Cave, Ground = "ground_cave", Side = "ground_cave", SideTint = new Color(0.82f, 0.8f, 0.78f), Inner = 0.9f,
                BlendStart = 0.6f, BlendEnd = 3.5f, Relief = BiomeRelief.Walls, Walls = BiomeWalls.Rock,
                WallLow = Ui.Hex("#9a8878"), WallHigh = Ui.Hex("#5e545e"), Detail = BiomeDetail.Scree, DetailTile = 3f,
                Cover = BiomeCover.Rubble, HillTrees = false,
                Grass = Ui.Hex("#6f7c5a"), GrassDark = Ui.Hex("#4c5642"), Bush = Ui.Hex("#56664a"),
                MapColor = Ui.Hex("#8c8078"),
            };
            t[IceCave] = new BiomeStyle
            {
                Id = IceCave, Ground = "ground_ice", Side = "ground_ice", SideTint = new Color(0.86f, 0.9f, 0.96f), Inner = 0.9f,
                BlendStart = 0.6f, BlendEnd = 3.5f, Relief = BiomeRelief.Walls, Walls = BiomeWalls.Ice,
                WallLow = Ui.Hex("#9cc2d8"), WallHigh = Ui.Hex("#56708e"), Detail = BiomeDetail.SnowDust, DetailTile = 4f,
                Cover = BiomeCover.Ice, HillTrees = false,
                Grass = Ui.Hex("#a4b8c4"), GrassDark = Ui.Hex("#7d92a4"), Bush = Ui.Hex("#8aa4b4"),
                MapColor = Ui.Hex("#bcd2e2"),
            };
            t[Crypt] = new BiomeStyle
            {
                Id = Crypt, Ground = "ground_crypt", Side = "ground_cave", SideTint = new Color(0.7f, 0.68f, 0.7f), Inner = 0.92f,
                BlendStart = 0.4f, BlendEnd = 2.5f, Relief = BiomeRelief.Walls, Walls = BiomeWalls.Masonry,
                WallLow = Ui.Hex("#8a8290"), WallHigh = Ui.Hex("#4a4552"), Detail = BiomeDetail.Scree, DetailTile = 3f,
                Cover = BiomeCover.Crypt, HillTrees = false,
                Grass = Ui.Hex("#6c7464"), GrassDark = Ui.Hex("#4a5048"), Bush = Ui.Hex("#56604e"),
                MapColor = Ui.Hex("#9a94a0"),
            };
            t[HollowHeart] = new BiomeStyle
            {
                Id = HollowHeart, Ground = "ground_hollow", Side = "ground_hollow", SideTint = new Color(0.8f, 0.76f, 0.84f), Inner = 0.92f,
                BlendStart = 0.6f, BlendEnd = 3.5f, Relief = BiomeRelief.Walls, Walls = BiomeWalls.Roots,
                WallLow = Ui.Hex("#5c4e5e"), WallHigh = Ui.Hex("#2f2a3a"), Detail = BiomeDetail.Roots, DetailTile = 5f,
                Cover = BiomeCover.Hollow, HillTrees = false,
                Grass = Ui.Hex("#5e6a58"), GrassDark = Ui.Hex("#3e4640"), Bush = Ui.Hex("#4c5848"),
                Flowers = new[] { Ui.Hex("#c49af0"), Ui.Hex("#8ee0d0") },
                MapColor = Ui.Hex("#7a6c86"),
            };
            return t;
        }
    }
}
