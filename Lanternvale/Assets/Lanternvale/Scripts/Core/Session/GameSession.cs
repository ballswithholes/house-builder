// The game session: the single object the Unity game-flow and UI layers talk to (Docs/SessionAPI.md).
// Split into partial files:
//   GameSession.cs          construction, NewGame, mode, events, settings
//   GameSession.Party.cs    party/roster/camp, companions, leader, formation, pets, XP & levels, approval
//   GameSession.Items.cs    inventory, equipment, item use, loot, vendors, trainers, talents, respec
//   GameSession.World.cs    maps, movement, triggers, interactions, dialogue, IDialogueContext
//   GameSession.Combat.cs   encounters -> Battle, AI stepping, leave combat, FinishBattle
//   GameSession.Time.cs     Tick, clock, out-of-combat regen/cooldowns/auras, resting
//   GameSession.Save.cs     save/load
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using Lanternvale.World;

namespace Lanternvale.Session
{
    public sealed partial class GameSession : IDialogueContext, IContentContext
    {
        /// <summary>Save format version written by <see cref="SaveGame"/>.</summary>
        public const int SaveVersion = 1;
        /// <summary>Member id of the main character (PartyMemberInfo.id, save data).</summary>
        public const string MainId = "player";
        /// <summary>Session-level outcome special: relight every spirit lantern.</summary>
        public const string RekindleLanternsSpecial = "RekindleLanterns";
        public const string LanternsFlag = "lanterns_rekindled";

        public readonly GameDatabase Db;
        /// <summary>The session's random source (battles, loot, skill checks). Its state is saved.</summary>
        public readonly Rng Rng;
        public readonly WorldState World;
        /// <summary>Dialogue state machine; the UI shows <c>Dialogue.Current</c>.</summary>
        public readonly DialogueRunner Dialogue;
        /// <summary>Shared party bags and gold. Mutate through the session (toasts, quests).</summary>
        public readonly Inventory Inventory = new Inventory();
        public SessionSettings Settings = new SessionSettings();
        /// <summary>Reason of the last failed command that returns an object instead of a reason string.</summary>
        public string LastError = "";

        bool hasGame;
        bool gameOver;
        /// <summary>True while state is being replaced (new game / load): dialogue context actions are ignored.</summary>
        bool resetting;

        // ------------------------------------------------------------------ events

        /// <summary>Raised synchronously for every session notification.</summary>
        public event Action<SessionEvent> EventRaised;
        /// <summary>Every CombatEvent of the field (exploration) context and of battles.</summary>
        public event Action<CombatEvent> CombatEventRaised;
        /// <summary>Queue events for <see cref="TakeEvents"/> (disable when only subscribing).</summary>
        public bool QueueEvents = true;
        readonly List<SessionEvent> queue = new List<SessionEvent>();

        public GameSession(GameDatabase db, ulong seed = 0)
        {
            Db = db ?? throw new ArgumentNullException(nameof(db));
            Rng = seed == 0 ? new Rng() : new Rng(seed);
            World = new WorldState(db, this);
            Dialogue = World.CreateDialogueRunner(Rng);
            Dialogue.Context = this;
            Dialogue.Ended += OnDialogueEnded;
            Dialogue.CheckRolled += OnCheckRolled;
            World.Quests.Changed += OnQuestEvent;
            Inventory.Changed += OnInventoryChanged;
        }

        // ------------------------------------------------------------------ mode

        public bool HasGame => hasGame;
        public bool IsGameOver => gameOver;

        public SessionMode Mode
        {
            get
            {
                if (!hasGame) return SessionMode.None;
                if (gameOver) return SessionMode.GameOver;
                if (Battle != null) return SessionMode.Combat;
                if (Dialogue.IsActive) return SessionMode.Dialogue;
                return SessionMode.Exploration;
            }
        }

        public bool InCombat => Battle != null;
        public bool IsExploring => Mode == SessionMode.Exploration;

        // ------------------------------------------------------------------ new game

        /// <summary>Starts a new game (see NewGameOptions).</summary>
        public void NewGame(string name, ClassId classId, int startLevel = 1, string sprite = "", bool veteranGear = true) =>
            NewGame(new NewGameOptions { Name = name, Class = classId, StartLevel = startLevel, Sprite = sprite ?? "", VeteranGear = veteranGear });

        public void NewGame(NewGameOptions o)
        {
            o ??= new NewGameOptions();
            if (Db.Class(o.Class) == null) throw new ArgumentException($"Unknown class {o.Class}");
            ResetState();
            var cfg = Db.Config;
            int level = MathUtil.Clamp(o.StartLevel, 1, Math.Max(1, cfg.maxLevel));
            bool veteran = level > 1;
            Settings.VeteranGear = veteran && o.VeteranGear;
            gameHour = MathUtil.Clamp(Settings.StartHour, 0f, 23.99f);
            day = 1;
            lastPhase = "";

            var leftovers = new List<ItemInstance>();
            var u = UnitFactory.CreateCharacter(Db, o.Class, string.IsNullOrEmpty(o.Name) ? "Traveller" : o.Name, level, veteran, leftovers);
            u.IsMainCharacter = true;
            if (!string.IsNullOrEmpty(o.Sprite)) u.Sprite = o.Sprite;
            if (!string.IsNullOrEmpty(o.Portrait)) u.Portrait = o.Portrait;
            if (veteran)
            {
                if (o.AutoAllocateTalents) Progression.AutoAllocateTalents(u);
                Progression.LearnAllAvailable(u);   // higher ranks of talent-granted abilities
                if (o.VeteranGear) leftovers.AddRange(StartingGear.EquipLevelGear(Db, u, Rng));
            }
            u.InvalidateStats();
            u.RestoreFull();
            roster.Add(u);
            party.Add(u);
            leader = u;
            hasGame = true;

            Inventory.Gold = Math.Max(0, cfg.startingGold) + (veteran ? StartingGear.VeteranGold(level) : 0);
            foreach (var it in leftovers) Inventory.Add(it);
            if (cfg.startingItems != null)
                foreach (var id in cfg.startingItems)
                {
                    var def = Db.Item(id);
                    if (def != null) Inventory.Add(def, 1);
                }

            Raise(new SessionEvent { Kind = SessionEventKind.GameStarted, Unit = u, Text = $"{u.Name} the {u.Class.name} arrives in the valley." });
            EnterMap(string.IsNullOrEmpty(cfg.startMap) ? FirstMapId() : cfg.startMap, string.IsNullOrEmpty(cfg.startSpawn) ? "default" : cfg.startSpawn);
            if (veteran && Progression.TalentPointsAvailable(u) > 0)
                Raise(new SessionEvent { Kind = SessionEventKind.TalentPointsAvailable, Unit = u, Amount = Progression.TalentPointsAvailable(u) });
            if (o.PlayOpening && !string.IsNullOrEmpty(cfg.startDialogue) && Db.Dialogues.ContainsKey(cfg.startDialogue))
                StartDialogue(cfg.startDialogue);
        }

        string FirstMapId()
        {
            foreach (var k in Db.Maps.Keys) return k;
            return "";
        }

        /// <summary>Clears every piece of game state (party, bags, world, map, battle, overlays, clock).</summary>
        void ResetState()
        {
            resetting = true;
            try
            {
                if (Dialogue.IsActive) Dialogue.End();
            }
            finally { resetting = false; }
            afterDialogue.Clear();
            dialogueEncounterId = "";
            Battle = null;
            BattleEncounter = null;
            battleCursor = 0;
            Field = null;
            roster.Clear();
            party.Clear();
            leader = null;
            approval.Clear();
            vendors.Clear();
            ActiveVendor = null;
            ActiveTrainer = null;
            ActiveRespecNpc = "";
            PendingLoot = null;
            Inventory.Items.Clear();
            Inventory.Gold = 0;
            resetting = true;
            try { World.Load(null); }
            finally { resetting = false; }
            MapId = "";
            Map = null;
            Nav = null;
            pathfinder = null;
            suppressedEncounters.Clear();
            transitionArmed = false;
            gameOver = false;
            hasGame = false;
            gameHour = 12f;
            day = 1;
            playSeconds = 0f;
            lastPhase = "";
            queue.Clear();
            Settings ??= new SessionSettings();   // player preferences survive NewGame; LoadGame applies the saved ones
            Settings.VeteranGear = false;
            LastError = "";
        }

        // ------------------------------------------------------------------ events

        /// <summary>Returns the queued events and clears the queue.</summary>
        public List<SessionEvent> TakeEvents()
        {
            var list = new List<SessionEvent>(queue);
            queue.Clear();
            return list;
        }

        /// <summary>Queued events not yet taken (read-only view).</summary>
        public IReadOnlyList<SessionEvent> PendingEvents => queue;

        internal void Raise(SessionEvent e)
        {
            if (e == null) return;
            e.Text ??= "";
            e.Id ??= "";
            e.Id2 ??= "";
            if (QueueEvents)
            {
                queue.Add(e);
                if (queue.Count > 2000) queue.RemoveRange(0, queue.Count - 2000);
            }
            EventRaised?.Invoke(e);
        }

        void Toast(string text)
        {
            if (!string.IsNullOrEmpty(text)) Raise(new SessionEvent { Kind = SessionEventKind.Toast, Text = text });
        }

        void ForwardCombatEvent(CombatEvent e) => CombatEventRaised?.Invoke(e);

        void OnQuestEvent(QuestEvent e)
        {
            if (resetting || e == null) return;
            SessionEventKind kind;
            switch (e.Kind)
            {
                case QuestEventKind.Started: kind = SessionEventKind.QuestStarted; break;
                case QuestEventKind.Completed: kind = SessionEventKind.QuestCompleted; break;
                case QuestEventKind.Failed: kind = SessionEventKind.QuestFailed; break;
                case QuestEventKind.RewardChoicePending: kind = SessionEventKind.QuestRewardChoice; break;
                default: kind = SessionEventKind.QuestUpdated; break;
            }
            string text = e.Text ?? "";
            switch (e.Kind)
            {
                case QuestEventKind.Started: text = "Quest started: " + e.QuestName; break;
                case QuestEventKind.Completed: text = "Quest completed: " + e.QuestName; break;
                case QuestEventKind.Failed: text = "Quest failed: " + e.QuestName; break;
                case QuestEventKind.RewardChoicePending: text = "Choose your reward: " + e.QuestName; break;
            }
            Raise(new SessionEvent { Kind = kind, Quest = e, Id = e.QuestId, Text = text });
        }

        void OnInventoryChanged(string itemId, int count)
        {
            if (resetting) return;
            World.Quests.OnItemCount(itemId, count);
        }

        // ------------------------------------------------------------------ helpers

        static string Money(int copper) => Inventory.FormatMoney(Math.Abs(copper));

        internal string ItemName(string itemId)
        {
            var d = Db.Item(itemId);
            return d != null ? d.name : itemId;
        }
    }
}
