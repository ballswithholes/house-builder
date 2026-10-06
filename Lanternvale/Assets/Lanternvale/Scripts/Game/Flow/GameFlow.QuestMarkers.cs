// GameFlow quest markers (Docs/Expansion.md §3, Docs/ThreeD.md "Quest markers"): a QuestMarker3D over every map NPC
// view (and unrecruited companion) that has a quest marker (GameSession.QuestMarkerOf). The markers are children of the
// NPC views, so they follow wanderers and die with DisposeView; entries the views lost (map change, RebuildWorld) are
// pruned here. Kinds are re-read when Session.QuestMarkersVersion moves; every frame they bob and face the camera.
// Hidden in combat, while a battle is presented, at game over, and over the NPC the party is talking to.
using System.Collections.Generic;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        readonly Dictionary<NpcEntry, QuestMarker3D> questMarkers = new Dictionary<NpcEntry, QuestMarker3D>();
        readonly List<NpcEntry> questMarkerGone = new List<NpcEntry>();
        int questMarkersVersion = -1;
        GameSession questMarkersSession;

        /// <summary>Id of the hovered map NPC or unrecruited companion ("" when the mouse is over anything else).</summary>
        public string HoveredNpcId =>
            HoveredKind == HoverKind.Npc && !ReferenceEquals(hoveredView, null) && npcByView.TryGetValue(hoveredView, out var e) ? e.Id ?? "" : "";

        /// <summary>Per frame (UpdateWorldTimers): attach, refresh, hide and animate the NPC quest markers.</summary>
        void UpdateQuestMarkers()
        {
            var s = Session;
            // forget markers whose NPC view is gone (their GameObjects died with the view)
            questMarkerGone.Clear();
            foreach (var kv in questMarkers)
                if (kv.Key.View == null || !npcEntries.Contains(kv.Key)) questMarkerGone.Add(kv.Key);
            foreach (var e in questMarkerGone)
            {
                var m = questMarkers[e];
                questMarkers.Remove(e);
                if (e.View != null) m.Dispose();
            }
            if (s == null || !HasGame) return;

            bool refresh = questMarkersSession != s || questMarkersVersion != s.QuestMarkersVersion;
            questMarkersSession = s;
            questMarkersVersion = s.QuestMarkersVersion;
            for (int i = 0; i < npcEntries.Count; i++)
            {
                var e = npcEntries[i];
                if (e.View == null || string.IsNullOrEmpty(e.Id)) continue;
                bool fresh = !questMarkers.TryGetValue(e, out var marker);
                if (!refresh && !fresh) continue;
                var info = s.QuestMarkerOf(e.Id);
                if (fresh)
                {
                    if (info.IsNone) continue;   // created on demand
                    marker = QuestMarker3D.Create(e.View.transform, (i * 0.37f) % 1f);
                    marker.Place(e.View.Height);
                    questMarkers[e] = marker;
                }
                marker.Set(info.Kind, info.Main);
            }

            var mode = s.Mode;
            bool hideAll = mode == SessionMode.Combat || mode == SessionMode.GameOver || battlePresenting || s.Battle != null || backdropActive;
            string speaker = mode == SessionMode.Dialogue ? s.Dialogue.OwnerId : null;
            var rig = CameraRig.Instance;
            float yaw = rig != null ? rig.Yaw : 0f, pitch = rig != null ? rig.Pitch : 40f;
            // a little larger when the camera is zoomed out, so a far "!" still reads
            float scale = rig != null ? Mathf.Lerp(1f, 1.35f, Mathf.InverseLerp(rig.DefaultSize, rig.MaxSize, rig.Zoom)) : 1f;
            float t = Time.time;
            foreach (var kv in questMarkers)
            {
                var e = kv.Key;
                var m = kv.Value;
                bool show = !hideAll && m.Kind != QuestMarker.None && e.View.Visible && (speaker == null || speaker != e.Id);
                m.SetVisible(show);
                if (show) m.Animate(t, yaw, pitch, scale);
            }
        }
    }
}
