// 三国志-霸王的世界 · 战场地形取自城池周边（对应网页版 js/battle-terrain.js，DESIGN-V3 §2）
// 【占位】规则移植阶段只提供最终的公开接口，供 BattleModel 调用；地形采样与生成由后续任务实现。
//   BattleTerrain.Water = 8（= Terrain.Water，湖海深水：不可通行，规则上等同山岳，外观为水面）
//   BattleTerrain.ApplyTo(model, setup) → bool：按 setup.target / setup.src（海路时 sea = GameState.LinkIsSea）生成战场，
//     写入 model.map / height / burning / castle（及 model.terrain）并返回 true；没有可用的战略地图（如无头测试）或生成失败时
//     什么也不改、返回 false，BattleModel 随即退回旧的随机地形（Generate(target.id × 31 + 年)）——与网页版相同。
// 交接：本文件归 UNITY-V3 §3「战场地形取自城池周边」任务所有，由该任务直接改写这个文件（不要另建同名类）。须保留上面的公开接口
//   （ApplyTo(BattleModel, BattleSetup) → bool 及其写入的 map / height / burning / castle / terrain，Water、IsWater、Name），
//   其测试（Tests~/battlemap/）应断言真实城池上 ApplyTo 返回 true——规则测试用的是 Tests~/sim/BattleTerrainStub.cs，发现不了占位未被替换。
//   在那之前游戏里的战场一律是旧的随机地形。
namespace Sanguo
{
    public static class BattleTerrain
    {
        public const int Water = 8;

        // 生成并写入战场地形；返回 false 时调用方沿用旧的随机地形
        public static bool ApplyTo(BattleModel m, BattleSetup s) { return false; }

        public static bool IsWater(Terrain t) { return (int)t == Water; }
        // 地块名（含「水域」）
        public static string Name(Terrain t) { return Defs.TerrainName(t); }
    }
}
