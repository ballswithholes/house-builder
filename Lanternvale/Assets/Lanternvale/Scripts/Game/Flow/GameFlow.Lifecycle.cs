// GameFlow lifecycle: boot, the main-menu backdrop (title diorama with a slow cinematic camera drift),
// new game / load / save / return to menu, and the per-frame loop that drives the session, the
// exploration input, the combat controller, the clock and the overlay.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        // ------------------------------------------------------------ state

        /// <summary>Incremented whenever the world presentation is rebuilt (map change, load, menu). Lets
        /// callers detect that a synchronous session call replaced the views under their feet.</summary>
        int worldGeneration;
        bool started;
        bool backdropActive, backdropTried;
        Transform menuCameraTarget;
        NavGrid backdropNav;
        float menuTime;
        bool pausedByMenu;
        float savedTimeScale = 1f;
        bool autosavePending;
        string autosaveReason = "";
        bool combatFallbackLogged;
        float stealthSyncTimer;
        GameMode lastMode = GameMode.Boot;
        Unit lastActiveUnit;

        const string BackdropMapId = "lanternvale";
        const float BackdropHour = 19.1f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            useGUILayout = false;
        }

        void Start()
        {
            started = true;
            if (!HasGame) BuildBackdrop();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (pausedByMenu || Time.timeScale != 1f) Time.timeScale = 1f;
            if (Session != null) Unsubscribe(Session);
            Instance = null;
        }

        // ------------------------------------------------------------ per-frame loop

        void Update()
        {
            float dt = Time.deltaTime;
            float unscaled = Time.unscaledDeltaTime;
            // Every stage has its own guard: one stage that throws every frame (a broken out-of-combat special in
            // Session.Tick, a bad pick…) must not freeze the others — the hotkeys (F5/F9) and the overlay (fades)
            // in particular always run. Repeating errors are logged once (see LogStageOnce).
            try { UpdatePause(); } catch (Exception e) { LogStageOnce("pause", e); }
            if (!HasGame)
            {
                try { UpdateMenu(unscaled); } catch (Exception e) { LogStageOnce("menu", e); }
            }
            else
            {
                var s = Session;
                // in combat only the play time advances: count real seconds, not the fast-forwarded presentation
                if (!pausedByMenu)
                {
                    try { s.Tick(s.Mode == SessionMode.Combat ? Mathf.Min(unscaled, 0.25f) : dt); }
                    catch (Exception e) { LogStageOnce("session-tick", e); }
                }
                if (Session == s && HasGame)
                {
                    try { UpdateGlobalHotkeys(); } catch (Exception e) { LogStageOnce("hotkeys", e); }
                }
                // a quick load / main menu replaced the session: skip the per-mode work this frame
                if (Session == s && HasGame)
                {
                    try
                    {
                        switch (s.Mode)
                        {
                            case SessionMode.Exploration:
                                UpdateExploration(dt);
                                break;
                            case SessionMode.Dialogue:
                                UpdateDialogueCamera();
                                break;
                            case SessionMode.Combat:
                                UpdateCombat(unscaled);
                                break;
                        }
                    }
                    catch (Exception e) { LogStageOnce("mode:" + s.Mode, e); }
                    if (Session == s)
                    {
                        try { UpdateHover(s.Mode); } catch (Exception e) { LogStageOnce("hover", e); }
                    }
                    try { UpdateWorldTimers(dt); } catch (Exception e) { LogStageOnce("world-timers", e); }
                }
            }
            try { UpdateClock(); } catch (Exception e) { LogStageOnce("clock", e); }
            try { UpdateWanderers(dt); } catch (Exception e) { LogStageOnce("wanderers", e); }
            try { UpdateDelayedRemovals(dt); } catch (Exception e) { LogStageOnce("removals", e); }
            try { UpdateLanternSequence(dt); } catch (Exception e) { LogStageOnce("lanterns", e); }
            try { UpdateOverlay(unscaled); } catch (Exception e) { LogStageOnce("overlay", e); }
            if (autosavePending)
            {
                try { RunAutosave(); }
                catch (Exception e) { autosavePending = false; LogStageOnce("autosave", e); }
            }
            try { SyncGameRootMode(); } catch (Exception e) { LogStageOnce("root-mode", e); }
        }

        // stage errors already logged (stage + type + message) and how many distinct ones each stage logged
        readonly HashSet<string> loggedStageErrors = new HashSet<string>();
        readonly Dictionary<string, int> stageErrorCounts = new Dictionary<string, int>();
        const int MaxDistinctErrorsPerStage = 6;

        /// <summary>Logs a frame-loop exception once per stage/type/message (a stage failing every frame would
        /// otherwise print ~60 stack traces a second); a stage stops logging after a few distinct errors.</summary>
        void LogStageOnce(string stage, Exception e)
        {
            if (e == null) return;
            string key = stage + "|" + e.GetType().FullName + "|" + e.Message;
            if (loggedStageErrors.Contains(key)) return;
            stageErrorCounts.TryGetValue(stage, out int n);
            if (n >= MaxDistinctErrorsPerStage) return;
            stageErrorCounts[stage] = n + 1;
            loggedStageErrors.Add(key);
            Debug.LogError("[Lanternvale] GameFlow stage '" + stage + "' failed (repeats of this error are not logged"
                           + (n + 1 == MaxDistinctErrorsPerStage ? "; further errors of this stage are muted" : "") + "):");
            Debug.LogException(e);
        }

        void UpdateCombat(float unscaledDt)
        {
            var c = Combat;
            if (c != null)
            {
                try { c.Update(unscaledDt); }
                catch (Exception e) { LogStageOnce("combat", e); }
                // Selected follows the active unit when a turn of one of ours begins (the UI may select another
                // member in between to inspect it)
                var active = Combat != null ? Combat.ActiveUnit : null;
                if (active != lastActiveUnit)
                {
                    lastActiveUnit = active;
                    if (active != null && IsPlayerSide(active)) SetSelectedInternal(active);
                }
                return;
            }
            // Safety net: the combat presenter is missing (e.g. it failed to construct). Resolve the fight with the
            // AI so the game never soft-locks, then let the normal CombatEnded flow run.
            var b = Session.Battle;
            if (b == null || combatFallbackLogged) return;   // one attempt per battle (F9 / main menu remain available)
            combatFallbackLogged = true;
            Debug.LogError("[Lanternvale] No CombatController for the running battle; auto-resolving it.");
            if (!b.IsOver) Session.AutoResolve(200);
            if (b.IsOver) Session.FinishBattle();
            else if (Session.CannotLeaveCombatReason() == null) Session.LeaveCombat();
            else Toast("The battle could not be resolved. Load a save to continue.");
        }

        bool IsPlayerSide(Unit u)
        {
            if (u == null || Session == null) return false;
            var b = Session.Battle;
            var team = b != null ? b.PlayerTeam : Team.Player;
            return u.Team == team && (Session.IsInParty(u) || (u.Owner != null && Session.IsInParty(u.Owner)));
        }

        void UpdateWorldTimers(float dt)
        {
            // stealth visuals of the party (cheap, but no need to do it every frame)
            stealthSyncTimer -= dt;
            if (stealthSyncTimer <= 0f)
            {
                stealthSyncTimer = 0.2f;
                SyncStealthVisuals();
            }
            // flags change from many places (dialogue outcomes, quests, battles): the session counts every change
            // (FlagsVersion, announced by FlagsChanged) — rebuild the flag-driven world only when it moved
            RefreshWorldIfFlagsChanged();
            UpdateQuestMarkers();   // NPC "!" / "?" (GameFlow.QuestMarkers.cs)
        }

        void UpdateClock()
        {
            DayNight.Paused = true;   // the session (or the backdrop) is the clock
            if (HasGame) DayNight.WorldHour = DisplayHour();
            else if (backdropActive) DayNight.WorldHour = BackdropHour + 0.35f * Mathf.Sin(menuTime * 0.02f);
        }

        void UpdatePause()
        {
            bool want = HasGame && UiRoot.IsOpen(UiPanels.Pause);
            if (want == pausedByMenu) return;
            pausedByMenu = want;
            if (want)
            {
                savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else Time.timeScale = ResumeTimeScale();
        }

        /// <summary>Time scale to restore after the pause menu: the combat presenter's speed only while the battle it
        /// was paused in is still being presented (a load or "main menu" from the pause screen disposed it).</summary>
        float ResumeTimeScale() => Combat != null && savedTimeScale > 0f ? savedTimeScale : 1f;

        /// <summary>
        /// Back to real time after the world was replaced (new game, load — also from the pause screen —, main menu): the
        /// pause is over, no fast-forward rate is remembered for a resume, Time.timeScale = 1. The combat presenter that set
        /// a faster scale is gone by then (DisposeWorld); a later fight applies its own speed again.
        /// </summary>
        void ResetTimeScale()
        {
            pausedByMenu = false;
            savedTimeScale = 1f;
            Time.timeScale = 1f;
        }

        void SyncGameRootMode()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            GameMode m;
            if (!HasGame)
            {
                // the menu UI may switch to CharacterCreation itself: leave that alone
                if (root.Mode == GameMode.CharacterCreation || (root.Mode == GameMode.MainMenu && lastMode == GameMode.MainMenu)) return;
                m = GameMode.MainMenu;
            }
            else
            {
                switch (Session.Mode)
                {
                    case SessionMode.Dialogue: m = GameMode.Dialogue; break;
                    case SessionMode.Combat: m = GameMode.Combat; break;
                    case SessionMode.GameOver: m = GameMode.GameOver; break;
                    default: m = GameMode.Exploration; break;
                }
            }
            if (m == lastMode && root.Mode == m) return;
            lastMode = m;
            root.SetMode(m);
        }

        // ------------------------------------------------------------ main menu backdrop

        void BuildBackdrop()
        {
            backdropTried = true;
            var db = Db;
            if (db == null || db.Maps.Count == 0) return;
            MapDef def = null;
            if (!db.Maps.TryGetValue(BackdropMapId, out def) || def == null)
            {
                var start = db.Config != null ? db.Config.startMap : "";
                if (string.IsNullOrEmpty(start) || !db.Maps.TryGetValue(start, out def) || def == null)
                    foreach (var m in db.Maps.Values) { def = m; break; }
            }
            if (def == null) return;
            try
            {
                DisposeWorld();
                var flags = new FlagStore();   // a fresh valley: no story flags set
                var view = MapView.Build(def, flags.Test);
                view.SetAllLanternsLit(true, false);   // the title shows the valley as it should be: every lantern glowing
                backdropActive = true;
                worldGeneration++;
                try { backdropNav = new NavGrid(def); }
                catch (Exception e) { backdropNav = null; Debug.LogWarning("[Lanternvale] Backdrop nav grid failed: " + e.Message); }
                if (def.npcs != null)
                    foreach (var n in def.npcs)
                        if (n != null && !string.IsNullOrEmpty(n.npc) && flags.Test(n.requireFlag) && !(n.hideFlag != "" && flags.Test(n.hideFlag)))
                            CreateNpcView(n, null);

                if (menuCameraTarget == null)
                {
                    var go = new GameObject("Menu Camera Target");
                    go.transform.SetParent(transform, false);
                    menuCameraTarget = go.transform;
                }
                menuTime = 0f;
                PlaceMenuCamera(0f);
                var rig = CameraRig.Instance;
                if (rig != null)
                {
                    rig.Follow = menuCameraTarget;
                    rig.Focus(null);
                    rig.ResetPan();
                    rig.AllowManualPan = false;
                    rig.Zoom = 6.6f;
                    rig.SnapToTarget();
                }
                DayNight.Paused = true;
                DayNight.WorldHour = BackdropHour;
                view.DayNight.MarkDirty();
                Music.Play("menu", 2f);
                BeginFadeIn(1.4f);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void UpdateMenu(float dt)
        {
            if (!backdropActive)
            {
                if (started && !backdropTried && MapView.Current == null && Db != null && Session == null) BuildBackdrop();
                return;
            }
            menuTime += dt;
            PlaceMenuCamera(menuTime);
        }

        /// <summary>Slow cinematic drift: an eased back-and-forth pan along the village with a gentle bob.</summary>
        void PlaceMenuCamera(float t)
        {
            var map = MapView.Current;
            if (menuCameraTarget == null || map == null || map.Def == null) return;
            float w = Mathf.Max(1f, map.Def.width), d = Mathf.Max(1f, map.Def.depth);
            const float period = 140f;
            float u = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f / period);
            float x = Mathf.Lerp(w * 0.16f, w * 0.84f, u);
            float y = d * 0.32f + Mathf.Sin(t * 0.11f) * 0.45f;
            menuCameraTarget.position = new Vector3(x, y, 0f);
        }

        // ------------------------------------------------------------ new game / load

        void StartNewGameInternal(NewGameOptions options)
        {
            LastError = "";
            if (Db == null) { LastError = "The game data is not loaded."; Toast(LastError); return; }
            var old = Session;
            var s = CreateSession();
            int gen = worldGeneration;
            // an autosave still pending for the old game (e.g. waiting for its opening conversation to end) must not
            // write the new one over the 'auto' slot: only requests raised by the new session's own events survive
            bool oldAutosave = autosavePending;
            string oldAutosaveReason = autosaveReason;
            autosavePending = false;
            autosaveReason = "";
            Session = s;
            try
            {
                s.NewGame(options ?? new NewGameOptions());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Unsubscribe(s);
                Session = old;
                autosavePending = oldAutosave;
                autosaveReason = oldAutosaveReason;
                string error = "Could not start a new game: " + e.Message;
                if (old == null || old.Mode == SessionMode.None) ReturnToMainMenuInternal();
                else if (gen != worldGeneration) RebuildWorld();   // the failed game had replaced the views: restore ours
                LastError = error;   // after ReturnToMainMenuInternal, which clears it
                Toast(LastError);
                return;
            }
            if (old != null && old != s) Unsubscribe(old);
            if (MapView.Current == null || backdropActive) RebuildWorld();   // MapEntered normally did this already
            UiRoot.CloseAll();
            ResetTimeScale();
            CloseSessionBoundPrompts();
            combatFallbackLogged = false;
        }

        /// <summary>Closes UI that holds closures bound to the replaced session (a right-click context menu over an
        /// item of the old bags). Yes/no prompts (ConfirmScreen) cannot be closed from outside: the quick save/load
        /// hotkeys are ignored while one is open instead (see UpdateGlobalHotkeys).</summary>
        static void CloseSessionBoundPrompts()
        {
            try { Panels.ContextMenuScreen.Close(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        string LoadFromSlotInternal(string slot)
        {
            LastError = "";
            var err = SaveFiles.Read(slot, out var json);
            if (err == null) err = LoadJson(json);
            if (err != null)
            {
                LastError = err;
                Debug.LogWarning($"[Lanternvale] Load '{slot}' failed: {err}");
                return err;
            }
            Toast("Loaded " + SaveFiles.DisplaySlot(SaveFiles.CleanSlot(slot)).ToLowerInvariant() + ".");
            return null;
        }

        string LoadJson(string json)
        {
            if (Db == null) return "The game data is not loaded.";
            var old = Session;
            var s = CreateSession();
            // a pending autosave belongs to the game being replaced (e.g. a new game still in its opening
            // conversation): it must not write the loaded game over the 'auto' slot
            bool oldAutosave = autosavePending;
            string oldAutosaveReason = autosaveReason;
            autosavePending = false;
            autosaveReason = "";
            Session = s;
            bool ok;
            string error;
            try { ok = s.LoadGame(json, out error); }
            catch (Exception e) { ok = false; error = "Load failed: " + e.Message; Debug.LogException(e); }
            if (!ok)
            {
                Unsubscribe(s);
                Session = old;
                autosavePending = oldAutosave;
                autosaveReason = oldAutosaveReason;
                return string.IsNullOrEmpty(error) ? "Load failed." : error;
            }
            if (old != null && old != s) Unsubscribe(old);
            if (MapView.Current == null || backdropActive) RebuildWorld();
            UiRoot.CloseAll();
            // a load from the pause screen (even mid-fight with fast-forward on): resume in real time
            ResetTimeScale();
            CloseSessionBoundPrompts();
            combatFallbackLogged = false;
            return null;
        }

        GameSession CreateSession()
        {
            var s = new GameSession(Db, 0) { QueueEvents = false };
            s.EventRaised += OnSessionEventRaised;
            s.CombatEventRaised += OnSessionCombatEvent;
            return s;
        }

        void Unsubscribe(GameSession s)
        {
            if (s == null) return;
            s.EventRaised -= OnSessionEventRaised;
            s.CombatEventRaised -= OnSessionCombatEvent;
        }

        string SaveToSlotInternal(string slot)
        {
            LastError = "";
            string err;
            if (!HasGame) err = "There is no game to save.";
            else if (SaveFiles.CleanSlot(slot).Length == 0) err = "Invalid save slot name.";
            else if (!Session.TrySaveGame(out var json, out var reason)) err = string.IsNullOrEmpty(reason) ? "Cannot save now." : reason;
            else err = SaveFiles.Write(slot, json);
            if (err != null) LastError = err;
            return err;
        }

        void RunAutosave()
        {
            if (!HasGame) { autosavePending = false; return; }
            // wait for a conversation to end (the opening, a dialogue right after travelling); drop it otherwise
            if (Session.Mode == SessionMode.Dialogue) return;
            autosavePending = false;
            if (Session.CannotSaveReason() != null) return;
            var err = SaveToSlotInternal("auto");
            if (err != null) Debug.LogWarning($"[Lanternvale] Autosave ({autosaveReason}) failed: {err}");
            else LastError = "";
        }

        void RequestAutosave(string reason)
        {
            autosavePending = true;
            autosaveReason = reason ?? "";
        }

        // ------------------------------------------------------------ main menu / teardown

        void ReturnToMainMenuInternal()
        {
            // a refused save ("Cannot save during a conversation.") must not greet the player in red on the title
            LastError = "";
            try
            {
                if (Session != null) Unsubscribe(Session);
                Session = null;
                autosavePending = false;
                autosaveReason = "";
                DisposeWorld();
                UiRoot.CloseAll();
                ResetTimeScale();   // the combat presenter (if any) is gone: never leave its pause or fast-forward behind
                backdropTried = false;
                BuildBackdrop();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Destroys every view, the combat presenter, the map and transient presentation state.</summary>
        void DisposeWorld()
        {
            worldGeneration++;
            if (Combat != null)
            {
                var c = Combat;
                Combat = null;
                try { c.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            }
            ResetExplorationState();
            ClearHoverState();
            Selected = null;
            lastActiveUnit = null;
            battlePresenting = false;
            DisposeAllViews();
            ClearLanternSequence();
            if (MapView.Current != null) MapView.Current.Dispose();
            backdropActive = false;
            backdropNav = null;
            try { FxSystem.ClearAll(); } catch (Exception e) { Debug.LogException(e); }
            FloatingText.Clear();
            ClearBarks();
            // a long-rest fade (and its pending wake-up) belongs to the old world: the next BeginFadeIn must apply
            fadePhase = FadePhase.None;
            restHoldHour = null;
            fadeAlpha = 0f;
            fadeHold = 0f;
            flashAlpha = 0f;
            var rig = CameraRig.Instance;
            if (rig != null)
            {
                rig.Follow = null;
                rig.Focus(null);
                rig.ResetPan();
                rig.AllowManualPan = true;
                // leaving mid-conversation (load, travel, main menu): give the player's own zoom back
                if (dialogueCamActive && zoomBeforeDialogue > 0f) rig.Zoom = zoomBeforeDialogue;
            }
            dialogueCamActive = false;
            zoomBeforeDialogue = -1f;
        }
    }
}
