// 无头规则测试用的 BattleTerrain 替身（与 Assets/Sanguo/Scripts/Battle/BattleTerrain.cs 的公开接口相同）。
// 网页版的 tests/sim.js 在 Node 下没有战略地图，SG.BattleTerrain.applyTo 不可用，BattleModel 一律退回旧的随机地形；
// 规则测试要与之逐条对照，所以这里 ApplyTo 恒返回 false，也免得真正的 BattleTerrain 依赖的画面代码（MapView 等）进规则测试。
// 战场地形本身的测试另在 Tests~/battlemap/。
namespace Sanguo
{
    public static class BattleTerrain
    {
        public const int Water = 8;
        public static bool ApplyTo(BattleModel m, BattleSetup s) { return false; }
        public static bool IsWater(Terrain t) { return (int)t == Water; }
        public static string Name(Terrain t) { return Defs.TerrainName(t); }
    }
}
