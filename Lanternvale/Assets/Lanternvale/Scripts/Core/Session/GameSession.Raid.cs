// Raids (Docs/Expansion.md §2.7, §6): a raid map lets a bigger party (up to MapDef.raidSize) in; leaving restores the
// normal party. CONTRACT STUB: these signatures are fixed; the raid-core builder owns the bodies. The stubs return
// defaults (never in a raid, normal party size everywhere, no candidates, entering is refused).
using System;
using System.Collections.Generic;
using Lanternvale.Rules;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        /// <summary>True while a raid party is active (on a raid map).</summary>
        public bool InRaid => false;

        /// <summary>Size of the active raid (0 when not in a raid).</summary>
        public int RaidSize => 0;

        /// <summary>The most characters the active party may hold on <paramref name="mapId"/>: the map's raidSize for a
        /// raid map, else config.partySize.</summary>
        public int MaxPartySizeOn(string mapId) => Math.Max(1, Db.Config.partySize);

        /// <summary>Characters the player may take into a raid (Main + every Active/Camp companion; Away ones excluded).</summary>
        public List<Unit> RaidCandidates() => new List<Unit>();

        /// <summary>Null when the party <paramref name="ids"/> (member ids, Main included) can enter the raid map now,
        /// else the reason.</summary>
        public string CannotEnterRaidReason(string mapId, IReadOnlyList<string> ids) => "Raids are not available yet.";

        /// <summary>Remembers the current party, forms the raid party from <paramref name="ids"/> (Main first, at most
        /// raidSize), optionally turns auto-play on for the companions, then travels to mapId/spawnId.
        /// Null on success, else the reason.</summary>
        public string EnterRaid(string mapId, string spawnId, IReadOnlyList<string> ids, bool companionsAutoPlay) =>
            CannotEnterRaidReason(mapId, ids);
    }
}
