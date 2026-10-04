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
        float stealthSyncTimer, worldRefreshTimer;
        GameMode lastMode = GameMode.Boot;

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
            if (!HasGame && !DioramaPreview.Active) BuildBackdrop();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (pausedByMenu) Time.timeScale = savedTimeScale;
            if (Session != null) Unsubscribe(Session);
            Instance = null;
        }

        // ------------------------------------------------------------ per-frame loop

        void Update()
        {
            float dt = Time.deltaTime;
            float unscaled = Time.unscaledDeltaTime;
            UpdatePause();
            try
            {
                if (!HasGame)
                {
                    UpdateMenu(unscaled);
                }
                else
                {
                    var s = Session;
                    if (!pausedByMenu) s.Tick(dt);
                    var mode = s.Mode;
                    switch (mode)
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
                    if (Session == s) UpdateHover(s.Mode);
                    UpdateWorldTimers(dt);
                }
                UpdateClock();
                UpdateWanderers(dt);
                UpdateDelayedRemovals(dt);
                UpdateLanternSequence(dt);
                UpdateOverlay(unscaled);
                if (autosavePending) RunAutosave();
                SyncGameRootMode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void UpdateCombat(float unscaledDt)
        {
            var c = Combat;
            if (c != null)
            {
                try { c.Update(unscaledDt); }
                catch (Exception e) { Debug.LogException(e); }
                // Selected follows the active unit when it is one of ours
                var active = Combat != null ? Combat.ActiveUnit : null;
                if (active != null && active != Selected && IsPlayerSide(active)) SetSelectedInternal(active);
                return;
            }
            // Safety net: the combat presenter is missing (e.g. it failed to construct). Resolve the fight with the
            // AI so the game never soft-locks, then let the normal CombatEnded flow run.
            var b = Session.Battle;
            if (b == null) return;
            if (!combatFallbackLogged)
            {
                combatFallbackLogged = true;
                Debug.LogError("[Lanternvale] No CombatController for the running battle; auto-resolving it.");
            }
            if (!b.IsOver) Session.AutoResolve(200);
            if (b.IsOver) Session.FinishBattle();
            else if (Session.CannotLeaveCombatReason() == null) Session.LeaveCombat();
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
            // flags can change from many places (dialogue outcomes, quests): re-evaluate the world now and then
            if (Session != null && Session.Mode == SessionMode.Exploration)
            {
                worldRefreshTimer -= dt;
                if (worldRefreshTimer <= 0f)
                {
                    worldRefreshTimer = 1f;
                    RefreshWorldFromFlags();
                }
            }
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
            else Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
        }

        void SyncGameRootMode()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            GameMode m;
            if (!HasGame) m = GameMode.MainMenu;
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
                if (started && !backdropTried && !DioramaPreview.Active && MapView.Current == null && Db != null && Session == null) BuildBackdrop();
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
                LastError = "Could not start a new game: " + e.Message;
                if (old == null || old.Mode == SessionMode.None) ReturnToMainMenuInternal();
                Toast(LastError);
                return;
            }
            if (old != null && old != s) Unsubscribe(old);
            if (MapView.Current == null || backdropActive) RebuildWorld();   // MapEntered normally did this already
            UiRoot.CloseAll();
            combatFallbackLogged = false;
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
            Session = s;
            bool ok;
            string error;
            try { ok = s.LoadGame(json, out error); }
            catch (Exception e) { ok = false; error = "Load failed: " + e.Message; Debug.LogException(e); }
            if (!ok)
            {
                Unsubscribe(s);
                Session = old;
                return string.IsNullOrEmpty(error) ? "Load failed." : error;
            }
            if (old != null && old != s) Unsubscribe(old);
            if (MapView.Current == null || backdropActive) RebuildWorld();
            UiRoot.CloseAll();
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
            autosavePending = false;
            if (!HasGame || Session.CannotSaveReason() != null) return;
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
            try
            {
                if (Session != null) Unsubscribe(Session);
                Session = null;
                DisposeWorld();
                UiRoot.CloseAll();
                if (pausedByMenu) { pausedByMenu = false; Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f; }
                backdropTried = false;
                if (!DioramaPreview.Active) BuildBackdrop();
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
            DisposeAllViews();
            ClearLanternSequence();
            if (MapView.Current != null) MapView.Current.Dispose();
            backdropActive = false;
            backdropNav = null;
            try { FxSystem.ClearAll(); } catch (Exception e) { Debug.LogException(e); }
            FloatingText.Clear();
            ClearBarks();
            var rig = CameraRig.Instance;
            if (rig != null)
            {
                rig.Follow = null;
                rig.Focus(null);
                rig.ResetPan();
                rig.AllowManualPan = true;
            }
        }
    }
}
