// Quest markers in map previews (`map … --markers auto|npc:kind,…`): the game's QuestMarker3D over the NPC posers,
// placed and turned to the camera as GameFlow.QuestMarkers does. `auto` evaluates the real marker rules
// (QuestMarkers.Evaluate) against a stub world built from --flags, --quests and --level; an explicit list names the
// kind per NPC (ready, available, progress, later; append "+main" for the main-quest look).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Game;
using Lanternvale.Util;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Preview
{
    public static class PreviewMarkers
    {
        public static int Build(MapScene scene, MapScene.Options opt)
        {
            var explicitKinds = new Dictionary<string, (QuestMarker kind, bool main)>(StringComparer.Ordinal);
            bool auto = string.Equals(opt.Markers, "auto", StringComparison.OrdinalIgnoreCase);
            if (!auto)
                foreach (var part in opt.Markers.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2) throw new ArgumentException("--markers expects auto or npc:kind[+main],… (got '" + part + "')");
                    string k = kv[1].Trim().ToLowerInvariant();
                    bool main = k.EndsWith("+main", StringComparison.Ordinal);
                    if (main) k = k.Substring(0, k.Length - 5);
                    var kind = k switch
                    {
                        "ready" or "turnin" or "?" => QuestMarker.ReadyToTurnIn,
                        "available" or "avail" or "!" => QuestMarker.Available,
                        "progress" or "inprogress" => QuestMarker.InProgress,
                        "later" => QuestMarker.AvailableLater,
                        _ => throw new ArgumentException("unknown marker kind '" + kv[1] + "' (ready, available, progress, later)"),
                    };
                    explicitKinds[kv[0].Trim()] = (kind, main);
                }
            PreviewWorldContext ctx = auto ? new PreviewWorldContext(scene.Db, opt) : null;
            var index = auto ? new QuestMarkerIndex(scene.Db) : null;
            int n = 0, i = 0;
            foreach (var kv in scene.NpcPosers)
            {
                QuestMarker kind;
                bool main;
                if (auto)
                {
                    var m = QuestMarkers.Best(QuestMarkers.Evaluate(index, ctx, ctx.World.DialogueMemory, kv.Key), kv.Key);
                    kind = m.Kind;
                    main = m.Main;
                    if (kind != QuestMarker.None) Console.WriteLine($"  marker {kv.Key}: {m.Kind} {m.QuestId}{(m.Main ? " (main)" : "")}");
                }
                else if (explicitKinds.TryGetValue(kv.Key, out var e)) { kind = e.kind; main = e.main; }
                else continue;
                if (kind == QuestMarker.None) continue;
                var poser = kv.Value;
                // as GameFlow.QuestMarkers: a child of the unit, HeadGap over its height, facing the camera
                var marker = QuestMarker3D.Create(poser.Holder, (i++ * 0.37f) % 1f);
                marker.Place(poser.Height);
                marker.Set(kind, main);
                marker.Animate(0.2f, opt.Yaw, scene.Pitch);
                n++;
            }
            return n;
        }
    }

    /// <summary>A still world for the marker rules: flags from --flags, quests from --quests (q, q=stage, q=done), a main character of --level.</summary>
    sealed class PreviewWorldContext : IDialogueContext
    {
        public readonly WorldState World;
        readonly List<PartyMemberInfo> party = new List<PartyMemberInfo>();

        public PreviewWorldContext(GameDatabase db, MapScene.Options opt)
        {
            World = new WorldState(db, this);
            party.Add(new PartyMemberInfo("player", "Preview", ClassId.Warrior, Math.Max(1, opt.Level), true));
            foreach (var f in opt.Flags) if (f != "*") World.Flags.Set(f, 1);
            foreach (var q in opt.Quests)
            {
                var kv = q.Split('=');
                string id = kv[0].Trim();
                if (!db.Quests.ContainsKey(id)) throw new ArgumentException("unknown quest '" + id + "' in --quests");
                string stage = kv.Length > 1 ? kv[1].Trim() : "";
                if (stage == "done") World.Quests.Complete(id);
                else if (stage.Length > 0) World.Quests.SetStage(id, stage);
                else World.Quests.Start(id);
            }
        }

        public FlagStore Flags => World.Flags;
        public QuestLog Quests => World.Quests;
        public string PlayerName => "Preview";
        public IReadOnlyList<PartyMemberInfo> Party => party;
        public int Gold => 0;
        public int CountItem(string itemId) => 0;
        public int GetApproval(string companionId) => 0;
        public string TimeOfDay => "day";
        public int SkillCheckBonus(string memberId, SkillCheck skill) => 0;
        public void GiveItem(string itemId, int count) { }
        public void TakeItem(string itemId, int count) { }
        public void GiveGold(int copper) { }
        public void TakeGold(int copper) { }
        public void GiveXP(int amount) { }
        public void Recruit(string companionId) { }
        public void Dismiss(string companionId) { }
        public void ChangeApproval(string companionId, int delta) { }
        public void StartCombat(string encounterId) { }
        public void OpenVendor(string npcId) { }
        public void OpenTrainer(string npcId) { }
        public void OpenRespec(string npcId) { }
        public void Rest() { }
        public void HealParty() { }
        public void Teleport(string mapId, string spawnId) { }
        public void RunSpecial(string specialId, OutcomeDef outcome) { }
    }
}
