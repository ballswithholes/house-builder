// Hidden things (Docs/Expansion.md §2.2, §7): passive region checks that reveal secrets, SecretFound when a hidden
// transition becomes visible (whatever set its revealFlag), props that start a dialogue, and the navigation grid kept in
// step with flag-gated props and chests (same NavGrid instance, Version++).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession
    {
        /// <summary>SecretFound banner: this prefix + the passage's label (else its target map's name).</summary>
        public const string SecretFoundPrefix = "You discovered a hidden passage: ";

        bool discoveryHooked;
        int secretsDeferred;
        bool navFlagsDirty;
        // "<map>/<transition>" of every hidden transition already announced (or visible when the game started or loaded)
        readonly HashSet<string> knownPassages = new HashSet<string>(StringComparer.Ordinal);
        List<KeyValuePair<MapDef, TransitionDef>> hiddenPassages;

        // ------------------------------------------------------------------ hooks (SetMap, CheckTriggers, EnterMap)

        /// <summary>Called by SetMap before the new grid is assigned. freshGame: the first map of a new or loaded game
        /// (ResetState cleared Nav): the passages visible now were found before (or never hidden) and are not announced.</summary>
        void OnDiscoveryMapSet(bool freshGame)
        {
            if (!discoveryHooked)
            {
                World.Flags.Changed += OnDiscoveryFlagChanged;
                discoveryHooked = true;
            }
            navFlagsDirty = false;
            if (!freshGame) return;
            knownPassages.Clear();
            foreach (var kv in HiddenPassages())
                if (MapRuntime.IsTransitionVisible(kv.Value, World.Flags)) knownPassages.Add(PassageKey(kv.Key, kv.Value));
        }

        void OnDiscoveryFlagChanged(string key, int oldValue, int newValue)
        {
            if (resetting || !hasGame) return;
            if (secretsDeferred == 0) AnnounceRevealedPassages();
            if (Nav == null) return;
            if (Battle != null) navFlagsDirty = true;    // never reshape the ground under a fight: refreshed once exploring
            else Nav.RefreshFlags();
        }

        /// <summary>Exploration step (CheckTriggers, map arrival): applies flag changes made during combat to the grid, then
        /// rolls the passive check of every region the leader stands in whose roll is due.</summary>
        void UpdateDiscovery()
        {
            RefreshNavIfDirty();
            if (!IsExploring || Map?.Def?.regions == null || Leader == null) return;
            var pos = Leader.Position;
            foreach (var r in Map.Def.regions)
                if (Map.IsRegionCheckDue(r, pos)) RollRegionCheck(r);
        }

        /// <summary>Applies flag changes made during a battle to the grid (RebuildField after a battle, CheckTriggers).</summary>
        void RefreshNavIfDirty()
        {
            if (!navFlagsDirty || Battle != null || Nav == null) return;
            navFlagsDirty = false;
            Nav.RefreshFlags();
        }

        void RollRegionCheck(RegionDef r)
        {
            // the check and its text come before the discovery banner its flag may trigger
            secretsDeferred++;
            CheckResult res;
            try
            {
                res = Map.RollRegionCheck(r, this, Rng);
                if (res != null)
                {
                    Raise(new SessionEvent { Kind = SessionEventKind.SkillCheck, Check = res, Id = r.id, Text = res.ToString() });
                    Toast(res.Success ? r.successText : r.failText);
                }
            }
            finally { secretsDeferred--; }
            AnnounceRevealedPassages();
        }

        // ------------------------------------------------------------------ hidden passages

        /// <summary>True when the passage (a hidden transition of any map) has been revealed: its revealFlag holds.</summary>
        public bool IsPassageRevealed(string mapId, string transitionId)
        {
            if (!Db.Maps.TryGetValue(mapId ?? "", out var m) || m.transitions == null) return false;
            foreach (var t in m.transitions)
                if (t != null && t.id == transitionId) return t.hidden && MapRuntime.IsTransitionVisible(t, World.Flags);
            return false;
        }

        /// <summary>Banner text of a hidden transition's discovery: "You discovered a hidden passage: &lt;label&gt;".</summary>
        public string SecretFoundText(TransitionDef t)
        {
            if (t == null) return "";
            string label = t.label;
            if (string.IsNullOrEmpty(label) && Db.Maps.TryGetValue(t.targetMap ?? "", out var target)) label = target.name;
            if (string.IsNullOrEmpty(label)) label = t.id;
            return SecretFoundPrefix + label;
        }

        // Raises SecretFound once for every hidden transition (any map) that has become visible since the game started
        // or loaded. A passage revealed under the leader's feet does not whisk the party away: it arms once they step out.
        void AnnounceRevealedPassages()
        {
            foreach (var kv in HiddenPassages())
            {
                var m = kv.Key;
                var t = kv.Value;
                if (!MapRuntime.IsTransitionVisible(t, World.Flags) || !knownPassages.Add(PassageKey(m, t))) continue;
                if (Map != null && m.id == MapId && Leader != null && MapRuntime.RectContains(t.pos, MapRuntime.TransitionSize(t), Leader.Position))
                    transitionArmed = false;
                Raise(new SessionEvent { Kind = SessionEventKind.SecretFound, Id = t.revealFlag, Id2 = t.id, Text = SecretFoundText(t) });
            }
        }

        List<KeyValuePair<MapDef, TransitionDef>> HiddenPassages()
        {
            if (hiddenPassages != null) return hiddenPassages;
            hiddenPassages = new List<KeyValuePair<MapDef, TransitionDef>>();
            foreach (var m in Db.Maps.Values)
            {
                if (m?.transitions == null) continue;
                foreach (var t in m.transitions)
                    if (t != null && t.hidden) hiddenPassages.Add(new KeyValuePair<MapDef, TransitionDef>(m, t));
            }
            return hiddenPassages;
        }

        static string PassageKey(MapDef m, TransitionDef t) => m.id + "/" + t.id;

        // ------------------------------------------------------------------ props

        /// <summary>
        /// Clicks a prop with an interact id. Hidden props (requireFlag/hideFlag) are not there. A prop with a dialogue
        /// starts it (owner = the interact id; exploration only) → Kind Dialogue; otherwise its text is toasted → Kind Text
        /// (Message = the text, "" when it has none).
        /// </summary>
        public InteractResult InteractProp(string interactId)
        {
            var p = Map?.FindProp(interactId);
            if (p == null) return InteractResult.Fail("There is nothing here.");
            if (!string.IsNullOrEmpty(p.dialogue) && Db.Dialogues.ContainsKey(p.dialogue))
            {
                if (!IsExploring) return InteractResult.Fail("Not now.");
                if (!StartDialogue(p.dialogue, interactId)) return InteractResult.Fail(LastError);
                return new InteractResult { Ok = true, Kind = InteractKind.Dialogue, Id = p.dialogue };
            }
            Toast(p.text);
            return new InteractResult { Ok = true, Kind = InteractKind.Text, Id = interactId, Message = p.text ?? "" };
        }
    }
}
