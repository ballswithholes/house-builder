// GameFlow reactions to SessionEvents. Every event is first handled here (map rebuilds, view sync, chests,
// lanterns, level-up sparkles, combat start/end, game over…) and then relayed to SessionEventRaised for the UI.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        static readonly Color LevelGold = new Color(1f, 0.85f, 0.42f);
        readonly Dictionary<Unit, int> levelFxFrame = new Dictionary<Unit, int>();

        // dialogue camera
        bool dialogueCamActive;
        float zoomBeforeDialogue = -1f;

        void OnSessionEventRaised(SessionEvent e)
        {
            if (e == null || Session == null) return;
            try { React(e); }
            catch (Exception ex) { Debug.LogException(ex); }
            Relay(e);
        }

        void React(SessionEvent e)
        {
            switch (e.Kind)
            {
                case SessionEventKind.GameStarted:
                    RequestAutosave("new game");     // runs once the opening conversation is over
                    break;
                case SessionEventKind.MapEntered:
                    RebuildWorld();
                    if (!string.IsNullOrEmpty(e.Id2) && e.Id2 != "default") RequestAutosave("travel");
                    break;
                case SessionEventKind.DialogueStarted:
                    OnDialogueStarted(e);
                    break;
                case SessionEventKind.DialogueEnded:
                    OnDialogueEnded();
                    break;
                case SessionEventKind.ChestOpened:
                    OnChestOpened(e.Id);
                    break;
                case SessionEventKind.SpecialOutcome:
                    if (e.Id == GameSession.RekindleLanternsSpecial) OnRekindleLanterns(e.Amount == 1);
                    break;
                case SessionEventKind.PartyChanged:
                case SessionEventKind.PetChanged:
                case SessionEventKind.CompanionRecruited:
                case SessionEventKind.CompanionDismissed:
                    if (!battlePresenting) SyncWorldViews();
                    break;
                case SessionEventKind.LeaderChanged:
                    OnLeaderChanged(e.Unit);
                    break;
                case SessionEventKind.LevelUp:
                    OnLevelUp(e.Unit, e.Amount);
                    break;
                case SessionEventKind.ItemReceived:
                    Sfx.Play("ui_open", null, 0.7f, 1.08f);
                    break;
                case SessionEventKind.GoldChanged:
                    if (e.Amount != 0) Sfx.Play("coin");
                    break;
                case SessionEventKind.QuestStarted:
                case SessionEventKind.QuestCompleted:
                    Sfx.Play("quest");
                    break;
                case SessionEventKind.SkillCheck:
                    OnSkillCheck(e);
                    break;
                case SessionEventKind.TransitionLocked:
                    OnTransitionLocked(e.Id);
                    break;
                case SessionEventKind.CombatStarted:
                    OnCombatStarted(e);
                    break;
                case SessionEventKind.CombatEnded:
                    OnCombatEnded(e);
                    break;
                case SessionEventKind.GameOver:
                    OnGameOver();
                    break;
                case SessionEventKind.Rested:
                    OnRested();
                    break;
                case SessionEventKind.PartyHealed:
                    OnPartyHealed();
                    break;
            }
        }

        // ------------------------------------------------------------ map

        /// <summary>Disposes the old diorama/views and builds the session's current map with every view.</summary>
        void RebuildWorld()
        {
            var s = Session;
            if (s == null || s.MapDef == null) return;
            DisposeWorld();
            var def = s.MapDef;
            var map = MapView.Build(def, s.Flags.Test);
            // chests opened in earlier visits stay open (no pop/sparkles)
            if (def.chests != null && s.Map != null)
                foreach (var c in def.chests)
                    if (c != null && s.Map.IsChestOpened(c.id)) map.SetChestOpen(c.id, true, false);
            if (s.LanternsRekindled) map.SetAllLanternsLit(true, false);

            SpawnWorldViews();

            var leader = s.Leader;
            SetSelectedInternal(leader);
            var rig = CameraRig.Instance;
            if (rig != null)
            {
                var lv = ViewOf(leader);
                rig.Follow = lv != null ? lv.transform : null;
                rig.Focus(null);
                rig.ResetPan();
                rig.AllowManualPan = true;
                rig.Zoom = rig.DefaultSize;
                rig.SnapToTarget();
            }
            dialogueCamActive = false;
            zoomBeforeDialogue = -1f;
            Music.Play(Music.MoodForMap(def), 2.5f);
            DayNight.Paused = true;
            DayNight.WorldHour = s.GameHour;
            map.DayNight.MarkDirty();
            worldRefreshTimer = 1f;
            stealthSyncTimer = 0f;
            BeginFadeIn(0.8f);
            // a dialogue may already be running (opening scene, loads never are): frame it right away
            if (s.Mode == SessionMode.Dialogue) OnDialogueStarted(null);
        }

        void OnChestOpened(string chestId)
        {
            var map = MapView.Current;
            if (map == null || string.IsNullOrEmpty(chestId)) return;
            map.SetChestOpen(chestId, true, true);
            var o = map.Find(chestId);
            Sfx.Play("chest_open", o != null ? o.Position : (Vector2?)null);
        }

        void OnTransitionLocked(string transitionId)
        {
            var map = MapView.Current;
            var o = map != null ? map.Find(transitionId) : null;
            if (o == null) return;
            map.SetLocked(transitionId, true);
            FxSystem.AuraPulse(o.Position, 1.2f, new Color(0.65f, 0.55f, 0.85f, 0.8f));
            Sfx.Play("debuff", o.Position, 0.6f, 1f);
        }

        // ------------------------------------------------------------ dialogue

        void OnDialogueStarted(SessionEvent e)
        {
            StopPartyMove(true);
            ResetExplorationState();
            var rig = CameraRig.Instance;
            if (rig == null) return;
            if (!dialogueCamActive)
            {
                zoomBeforeDialogue = rig.Zoom;
                rig.Zoom = Mathf.Min(rig.Zoom, 5.3f);
            }
            dialogueCamActive = true;
            UpdateDialogueCamera();
        }

        void OnDialogueEnded()
        {
            var rig = CameraRig.Instance;
            if (rig != null && dialogueCamActive)
            {
                rig.Focus(null);
                if (zoomBeforeDialogue > 0f) rig.Zoom = zoomBeforeDialogue;
            }
            dialogueCamActive = false;
            zoomBeforeDialogue = -1f;
            // flags may have changed (recruits, quests, peaceful encounters): refresh the world
            if (Session != null && !battlePresenting && Session.Battle == null && MapView.Current != null)
            {
                MapView.Current.RefreshFlags(Session.Flags.Test);
                SyncWorldViews();
            }
        }

        /// <summary>Keeps the camera between the leader and the current speaker; the two face each other.</summary>
        void UpdateDialogueCamera()
        {
            var s = Session;
            var rig = CameraRig.Instance;
            if (s == null || rig == null || !s.Dialogue.IsActive) return;
            if (!dialogueCamActive) { OnDialogueStarted(null); return; }
            var leader = ViewOf(s.Leader);
            if (leader == null) return;
            var cur = s.Dialogue.Current;
            string speakerId = cur != null ? cur.SpeakerId : "";
            Vector2? speaker = ResolveSpeaker(speakerId, s.Dialogue.OwnerId, leader);
            var lp = leader.FeetPosition;
            Vector2 focus = lp;
            if (speaker.HasValue)
            {
                var sp = speaker.Value;
                if ((sp - lp).sqrMagnitude < 14f * 14f) focus = (lp + sp) * 0.5f;
                if (!leader.IsMoving) leader.FaceTowards(sp);
                var sv = speakerView;
                if (sv != null && !sv.IsMoving && sv != leader) sv.FaceTowards(lp);
            }
            rig.Focus(focus);
        }

        UnitView speakerView;

        /// <summary>World position of a dialogue speaker: NPC, party member, encounter, or the dialogue owner.</summary>
        Vector2? ResolveSpeaker(string speakerId, string ownerId, UnitView leader)
        {
            speakerView = null;
            var s = Session;
            if (string.IsNullOrEmpty(speakerId) || speakerId == "narrator") speakerId = ownerId;
            if (string.IsNullOrEmpty(speakerId)) return null;
            if (speakerId == GameSession.MainId || speakerId == "player")
            {
                // the hero speaks: frame the owner instead so both stay in view
                if (!string.IsNullOrEmpty(ownerId) && ownerId != speakerId) return ResolveSpeaker(ownerId, "", leader);
                return null;
            }
            var ne = FindNpcEntry(speakerId);
            if (ne != null && ne.View != null) { speakerView = ne.View; return ne.View.FeetPosition; }
            var member = s.FindMember(speakerId);
            var mv = ViewOf(member);
            if (mv != null && mv != leader) { speakerView = mv; return mv.FeetPosition; }
            // encounter owner: the centre of its visible enemies
            int n = 0;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < enemyEntries.Count; i++)
            {
                var ee = enemyEntries[i];
                if (ee.Encounter == null || ee.Encounter.id != speakerId || ee.View == null) continue;
                if (speakerView == null) speakerView = ee.View;
                sum += ee.View.FeetPosition;
                n++;
            }
            if (n > 0) return sum / n;
            if (s.Map != null)
            {
                var enc = s.Map.FindEncounter(speakerId);
                if (enc != null) return ToUnity(enc.pos);
            }
            return null;
        }

        // ------------------------------------------------------------ party

        void OnLeaderChanged(Unit leader)
        {
            if (leader == null) return;
            var lv = ViewOf(leader);
            var rig = CameraRig.Instance;
            if (rig != null && lv != null && (Session == null || Session.Battle == null))
            {
                rig.Follow = lv.transform;
                rig.ResetPan();
            }
            SetSelectedInternal(leader);
        }

        void OnLevelUp(Unit u, int level)
        {
            var v = ViewOf(u);
            if (v == null) { Sfx.Play("level_up"); return; }
            // several level-ups can arrive at once (big XP rewards): one celebration per unit and frame
            int frame = Time.frameCount;
            if (levelFxFrame.TryGetValue(u, out var f) && f == frame) return;
            levelFxFrame[u] = frame;
            if (levelFxFrame.Count > 32) levelFxFrame.Clear();
            Sfx.Play("level_up", v.FeetPosition);
            FxSystem.Sparkles(v.CenterPosition, LevelGold, 26);
            FxSystem.AuraPulse(v.FeetPosition, 1.8f, new Color(LevelGold.r, LevelGold.g, LevelGold.b, 0.9f));
            FxSystem.HealSparkles(v.FeetPosition, v.Height);
            FloatingText.Spawn(v.HeadPosition + new Vector2(0f, 0.25f), level > 0 ? "Level " + level + "!" : "Level up!", LevelGold, 1.15f, true);
        }

        void OnSkillCheck(SessionEvent e)
        {
            var c = e.Check;
            if (c == null || Session == null) return;
            // dialogue checks are shown by the dialogue window; world checks (locks) get a little floating verdict
            if (Session.Mode == SessionMode.Dialogue) return;
            var roller = Session.FindMember(c.RollerId);
            var v = ViewOf(roller) ?? ViewOf(Session.Leader);
            if (v == null) return;
            var col = c.Success ? Ui.Good : Ui.Bad;
            string word = c.Critical ? "Critical success!" : c.Fumble ? "Critical failure" : c.Success ? "Success" : "Failed";
            FloatingText.Spawn(v.HeadPosition + new Vector2(0f, 0.2f), $"{c.Total} vs {c.Dc} · {word}", col, 0.9f, c.Critical);
            Sfx.Play(c.Success ? "buff" : "debuff", v.FeetPosition, 0.7f, 1f);
        }

        void OnPartyHealed()
        {
            if (Session == null) return;
            foreach (var u in Session.PartyUnits())
            {
                var v = ViewOf(u);
                if (v == null) continue;
                if (v.IsDowned) v.PlayRevive();
                FxSystem.HealSparkles(v.FeetPosition, v.Height);
            }
            Sfx.Play("heal");
        }

        void OnGameOver()
        {
            StopPartyMove(false);
            ResetExplorationState();
            ClearHoverState();
            Music.Stop(4f);
            var rig = CameraRig.Instance;
            if (rig != null) rig.Focus(null);
        }

        // ------------------------------------------------------------ combat

        void OnCombatStarted(SessionEvent e)
        {
            var s = Session;
            var b = e.Battle ?? s.Battle;
            if (b == null) return;
            battlePresenting = true;
            StopPartyMove(false);
            ResetExplorationState();
            ClearHoverState();
            if (MapView.Current != null) MapView.Current.ClearHighlights();
            dialogueCamActive = false;
            var rig = CameraRig.Instance;
            if (rig != null)
            {
                rig.Focus(null);
                if (zoomBeforeDialogue > 0f) rig.Zoom = zoomBeforeDialogue;
            }
            zoomBeforeDialogue = -1f;

            // the exploration views of this encounter become the battle units' views
            var enc = s.BattleEncounter;
            List<EnemyEntry> pool = null;
            if (enc != null)
                for (int i = enemyEntries.Count - 1; i >= 0; i--)
                    if (enemyEntries[i].Encounter == enc)
                    {
                        (pool ??= new List<EnemyEntry>()).Add(enemyEntries[i]);
                        enemyByView.Remove(enemyEntries[i].View);
                        enemyEntries.RemoveAt(i);
                    }

            foreach (var u in b.Units)
            {
                if (ViewOf(u) != null) continue;
                UnitView v = null;
                if (pool != null && u.Creature != null && u.Team != b.PlayerTeam)
                {
                    int best = -1;
                    float bestD = float.MaxValue;
                    var up = ToUnity(u.Position);
                    for (int i = 0; i < pool.Count; i++)
                    {
                        if (pool[i].View == null || pool[i].Creature == null || pool[i].Creature.id != u.Creature.id) continue;
                        float d = (pool[i].View.FeetPosition - up).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                    if (best >= 0)
                    {
                        v = pool[best].View;
                        pool.RemoveAt(best);
                        v.SetHovered(false);
                        BindView(u, v);
                        v.DisplayName = NameOf(u);
                        v.RingColor = RingColorOf(u);
                    }
                }
                if (v == null)
                {
                    v = EnsureView(u);
                    if (v != null && u.Team != b.PlayerTeam)
                    {
                        // ambushes (hidden encounters) appear in a puff of leaves and smoke
                        FxSystem.Puff(v.CenterPosition, new Color(0.8f, 0.82f, 0.78f), 1.1f);
                        FxSystem.Sparkles(v.CenterPosition, new Color(0.75f, 0.7f, 0.9f), 8);
                    }
                }
            }
            if (pool != null) foreach (var left in pool) if (left.View != null) DisposeView(left.View);

            // everyone steps to the battle positions the session chose
            foreach (var u in b.Units)
            {
                var v = ViewOf(u);
                if (v == null) continue;
                var target = ToUnity(u.Position);
                if ((v.FeetPosition - target).sqrMagnitude > 0.0025f)
                    v.MoveAlong(new List<Vector2>(1) { target }, 4.5f, null);
                else
                {
                    v.StopMoving();
                    if (Mathf.Abs(u.Facing.x) > 0.05f) v.SetFacing(u.Facing.x < 0f ? -1 : 1);
                }
            }

            // field-only summons (out-of-combat totems…) that did not join the fight
            tmpUnits.Clear();
            foreach (var kv in views)
            {
                var u = kv.Key;
                if (b.Units.Contains(u)) continue;
                if (u.Kind == UnitKind.Totem || u.Kind == UnitKind.Summon || (u.Kind == UnitKind.Pet && !u.IsPersistentPet)) tmpUnits.Add(u);
            }
            foreach (var u in tmpUnits) RemoveView(u);

            Music.Play("combat", 1.5f);
            combatFallbackLogged = false;
            try { Combat = new CombatController(this, b); }
            catch (Exception ex)
            {
                Combat = null;
                Debug.LogException(ex);
            }
        }

        void OnCombatEnded(SessionEvent e)
        {
            var s = Session;
            var b = e.Battle;
            var c = Combat;
            Combat = null;
            battlePresenting = false;
            if (c != null)
            {
                try { c.Dispose(); } catch (Exception ex) { Debug.LogException(ex); }
            }
            ClearHoverState();

            // enemies: the dead fade out, the living (practice dummies, a fight left early) go back to being encounter views
            List<KeyValuePair<Unit, UnitView>> living = null;
            if (b != null)
            {
                foreach (var u in b.Units)
                {
                    if (u.Team == b.PlayerTeam) continue;
                    var v = ViewOf(u);
                    if (v == null) continue;
                    DetachUnitView(u);
                    v.SetTargetable(null);
                    v.SetActiveTurn(false);
                    v.StopCasting();
                    if (!u.IsAlive || v.IsDead)
                    {
                        if (!v.IsDead) v.PlayDeath();
                        ScheduleRemoval(v, 1.4f);
                    }
                    else (living ??= new List<KeyValuePair<Unit, UnitView>>()).Add(new KeyValuePair<Unit, UnitView>(u, v));
                }
            }

            // allies are back on their feet after a win; clear combat visuals
            foreach (var kv in views)
            {
                var u = kv.Key;
                var v = kv.Value;
                if (v == null) continue;
                v.SetTargetable(null);
                v.SetActiveTurn(false);
                v.StopCasting();
                if (u.IsAlive && v.IsDowned) v.PlayRevive();
            }

            if (e.Outcome != CombatEndKind.Defeat && s != null)
            {
                SyncWorldViews(living);
                // party members walk to where the session has them (should already match)
                foreach (var u in s.PartyUnits())
                {
                    var v = ViewOf(u);
                    if (v == null || v.IsMoving) continue;
                    var p = ToUnity(u.Position);
                    if ((v.FeetPosition - p).sqrMagnitude > 0.09f) WalkView(v, p, 3.2f, null);
                }
                Music.Play(Music.MoodForMap(s.MapDef), 3f);
                if (e.Outcome == CombatEndKind.Victory) RequestAutosave("victory");
            }
            if (living != null)
                foreach (var kv in living)
                    if (kv.Value != null && !enemyByView.ContainsKey(kv.Value)) DisposeView(kv.Value);

            var rig = CameraRig.Instance;
            if (rig != null && s != null)
            {
                var lv = ViewOf(s.Leader);
                if (lv != null) rig.Follow = lv.transform;
                rig.Focus(null);
            }
            if (s != null && (Selected == null || !s.IsInParty(Selected))) SetSelectedInternal(s.Leader);
        }
    }
}
