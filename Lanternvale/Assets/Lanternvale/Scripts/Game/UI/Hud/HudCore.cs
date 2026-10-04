// HUD core: shared state queries, layout, cached text, styles and allocation-free drawing helpers used by every
// HUD layer in this folder (party frames, action bar, turn economy, turn order, target frame, nameplates,
// toasts/banners, quest tracker, combat log). See Docs/UI_HUD.md.
//
// Rules of the folder:
//   * HUD layers are IUiScreen classes discovered by UiRoot (Order 0–99, Id "" — except the combat log panel).
//   * Plain GUI with Rects only (no GUILayout), so Layout/Repaint can never disagree.
//   * State that drives what is drawn is snapshotted in Tick (Update) or at the top of Draw.
//   * Clicks never mutate game state inside OnGUI: they are posted (Hud.Post) and run from Tick.
//   * Textures/styles/strings are cached; hot paths avoid allocations.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    /// <summary>Shared HUD state: what is visible this frame, who is acting, hover hand-offs, posted commands.</summary>
    public static class Hud
    {
        // ------------------------------------------------------------ draw orders (HUD band 0–99)
        public const int OrderNameplates = 2;
        public const int OrderPartyFrames = 10;
        public const int OrderTurnOrder = 14;
        public const int OrderTargetFrame = 16;
        public const int OrderQuestTracker = 18;
        public const int OrderActionBar = 20;
        public const int OrderMenuBar = 22;
        public const int OrderTurn = 24;
        public const int OrderCompactLog = 26;
        public const int OrderToasts = 60;
        public const int OrderSelfRes = 80;
        // the toggled log panel lives in the panel band, below every other window (vendor 110, trainer 111, bags 126…):
        // occlusion tests "Order > order" strictly, so two windows must never share an order
        public const int OrderCombatLogPanel = 105;

        // ------------------------------------------------------------ accessors
        public static GameFlow Flow => GameFlow.Instance;
        public static GameSession Session { get { var f = GameFlow.Instance; return f != null ? f.Session : null; } }
        public static CombatController Combat { get { var f = GameFlow.Instance; return f != null ? f.Combat : null; } }
        public static Battle Battle { get { var c = Combat; return c != null ? c.Battle : null; } }
        public static GameDatabase Db => GameRoot.Instance != null ? GameRoot.Instance.Db : null;

        // ------------------------------------------------------------ per-frame visibility snapshot
        static int stateFrame = -1;
        static bool sWorld, sCombat, sExplore, sToasts, sDialogue, sMenus;

        static void RefreshState()
        {
            int f = Time.frameCount;
            if (f == stateFrame) return;
            stateFrame = f;
            sWorld = sCombat = sExplore = sToasts = sDialogue = sMenus = false;
            try
            {
                if (!GameFlow.HasGame) return;
                var s = Session;
                var mode = s.Mode;
                sMenus = FullscreenPanelOpen();
                bool modal = UiRoot.ModalActive;
                sDialogue = mode == SessionMode.Dialogue;
                sToasts = mode != SessionMode.GameOver && mode != SessionMode.None && !sMenus;
                bool hudOk = !modal && !sMenus;
                sCombat = hudOk && mode == SessionMode.Combat && Combat != null && Combat.Battle != null;
                sExplore = hudOk && mode == SessionMode.Exploration;
                sWorld = sCombat || sExplore;
            }
            catch (Exception e) { LogOnce("state", "HUD state: " + e.Message); }
        }

        static bool FullscreenPanelOpen() =>
            UiRoot.IsOpen(UiPanels.Pause) || UiRoot.IsOpen(UiPanels.Settings) || UiRoot.IsOpen(UiPanels.SaveLoad) || UiRoot.IsOpen(UiPanels.Map);

        /// <summary>Exploration or combat, no modal window / full-screen menu, not in dialogue.</summary>
        public static bool WorldHud { get { RefreshState(); return sWorld; } }
        /// <summary>WorldHud and a battle is being presented.</summary>
        public static bool CombatHud { get { RefreshState(); return sCombat; } }
        /// <summary>WorldHud in exploration.</summary>
        public static bool ExploreHud { get { RefreshState(); return sExplore; } }
        /// <summary>Toasts/banners: any running game except game over and full-screen menus (also during dialogue).</summary>
        public static bool ToastsVisible { get { RefreshState(); return sToasts; } }
        public static bool InDialogue { get { RefreshState(); return sDialogue; } }

        // ------------------------------------------------------------ who acts
        // Both follow the PRESENTED turn (HudPresented.ActiveUnit): while the previous unit's blows / end-of-turn swings
        // are still being animated, the HUD keeps showing that unit; it switches when the next TurnStart is shown.

        /// <summary>The acting battle unit when it is player-controlled (its bar/turn economy are shown), else null.</summary>
        public static Unit ActivePlayerUnit
        {
            get
            {
                var b = Battle;
                if (b == null || b.IsOver) return null;
                var u = HudPresented.ActiveUnit(b);
                if (u == null || u.Team != b.PlayerTeam) return null;
                try { return b.IsAIControlled(u) ? null : u; }
                catch (Exception) { return null; }
            }
        }

        /// <summary>The acting battle unit when an AI plays it (enemy, auto-played companion, pet, controlled unit).</summary>
        public static Unit ActiveAiUnit
        {
            get
            {
                var b = Battle;
                if (b == null || b.IsOver) return null;
                var u = HudPresented.ActiveUnit(b);
                if (u == null) return null;
                try { return b.IsAIControlled(u) ? u : null; }
                catch (Exception) { return null; }
            }
        }

        // ------------------------------------------------------------ hand-offs between layers (same frame)
        static AbilityStatus hoveredSlot;
        static int hoveredSlotFrame = -1;

        /// <summary>Action-bar slot under the mouse this frame (turn economy previews its Time cost).</summary>
        public static AbilityStatus HoveredSlot
        {
            get => hoveredSlotFrame == Time.frameCount ? hoveredSlot : null;
            set { hoveredSlot = value; hoveredSlotFrame = Time.frameCount; }
        }

        /// <summary>The unit shown in the target frame this frame (nameplates outline it).</summary>
        public static Unit FramedTarget;

        /// <summary>Snapshot of the action bar unit's statuses (target frame looks for usable interrupts).</summary>
        public static readonly List<AbilityStatus> BarStatuses = new List<AbilityStatus>();
        public static Unit BarUnit;
        /// <summary>Hotkey label of a bar ability currently on the visible page ("" if not visible).</summary>
        public static Func<string, string> HotkeyOf;

        // ------------------------------------------------------------ out-of-combat "choose a party member" mode
        public sealed class FieldPickState
        {
            public Unit Caster;
            public AbilityDef Ability;
            public ItemInstance Item;
            public TargetType Target;
            /// <summary>Pinned rank to cast (0 = the highest known rank).</summary>
            public int Rank;
            public string Name => Item != null ? Item.Name : (Ability != null ? Ability.name : "");
            string hint;
            /// <summary>The instruction line shown while choosing (built once).</summary>
            public string Hint => hint ??= Target == TargetType.DeadAlly
                ? "Choose who to bring back with " + Name + " — click a portrait · right-click to cancel"
                : "Choose who receives " + Name + " — click a portrait · right-click to cancel · press again for yourself";
            public string Id => Item != null ? "item:" + Item.Def.id : (Ability != null ? Ability.id : "");
        }

        /// <summary>Non-null while the player chooses which party member receives an ally ability/item out of combat.</summary>
        public static FieldPickState FieldPick;

        public static bool IsValidPickTarget(Unit u)
        {
            var p = FieldPick;
            if (p == null || u == null) return false;
            switch (p.Target)
            {
                case TargetType.DeadAlly: return u.Dead || u.Downed;
                case TargetType.AllyOther: return u != p.Caster && !u.Dead;
                default: return !u.Dead;
            }
        }

        /// <summary>Uses the picked ability/item on u (posted; runs from Tick).</summary>
        public static void ResolveFieldPick(Unit u)
        {
            var p = FieldPick;
            if (p == null || u == null) return;
            if (!IsValidPickTarget(u))
            {
                Error(p.Target == TargetType.DeadAlly ? "That party member is not dead." : "Invalid target.");
                return;
            }
            FieldPick = null;
            Post(() => UseInField(p.Caster, p.Ability, p.Item, u, p.Rank));
        }

        /// <summary>
        /// A unit clicked in the HUD (party frame, pet frame, turn order): in combat while an ability/item is being
        /// targeted it confirms that target (CombatController.TargetUnit), otherwise it selects the unit. Posted.
        /// </summary>
        public static void ClickUnit(Unit u)
        {
            if (u == null) return;
            Post(() =>
            {
                var c = Combat;
                if (c != null && c.IsTargeting)
                {
                    c.TargetUnit(u);   // failures arrive through Combat.LastError (red error lane)
                    return;
                }
                Flow?.Select(u);
            });
        }

        // ------------------------------------------------------------ combat targeting from the HUD (party frames)

        /// <summary>How a HUD-clicked unit would answer the ability/item being targeted in combat.</summary>
        public enum TargetState { None, Valid, Reachable, Invalid }

        static readonly Dictionary<Unit, TargetState> targetStates = new Dictionary<Unit, TargetState>();
        static int targetStamp = -1;
        static AbilityDef targetAbility;
        static int targetRank = -1;
        static Unit targetActor;

        /// <summary>
        /// While the player targets an ability/item in combat: Valid (a click on the unit's frame casts it now), Reachable
        /// (out of range or sight, a click walks into range first when the movement allows), Invalid; None when nothing is
        /// targeted. Cached until the battle, the ability or its rank changes.
        /// </summary>
        public static TargetState TargetStateOf(Unit u)
        {
            var c = Combat;
            var b = c != null ? c.Battle : null;
            if (u == null || b == null || !c.IsTargeting || !c.IsPlayerTurn) return TargetState.None;
            var a = c.TargetingAbility;
            var actor = b.ActiveUnit;
            if (a == null || actor == null) return TargetState.None;
            int rank = c.TargetingItem != null ? 0 : c.TargetingRank;
            int stamp = b.Events.Count;
            if (stamp != targetStamp || a != targetAbility || rank != targetRank || actor != targetActor)
            {
                targetStates.Clear();
                targetStamp = stamp;
                targetAbility = a;
                targetRank = rank;
                targetActor = actor;
            }
            if (targetStates.TryGetValue(u, out var st)) return st;
            st = TargetState.Invalid;
            try
            {
                if (a.target == TargetType.Point || a.target == TargetType.Self || a.target == TargetType.Pet) st = TargetState.None;
                else if (b.Units.Contains(u))
                {
                    var chk = b.CanUse(actor, a, u, null, c.TargetingItem != null, rank);
                    if (chk.Ok) st = TargetState.Valid;
                    else if (chk.Code == UseFailure.Range || chk.Code == UseFailure.LineOfSight) st = TargetState.Reachable;
                }
            }
            catch (Exception) { st = TargetState.Invalid; }
            targetStates[u] = st;
            return st;
        }

        public static void UseInField(Unit caster, AbilityDef a, ItemInstance item, Unit target, int rank = 0)
        {
            var f = Flow;
            if (f == null || caster == null) return;
            string why;
            if (item != null) why = f.UseItemOutOfCombat(caster, item, target);
            else if (a != null) why = f.UseAbilityOutOfCombat(caster, a.id, target, rank);
            else return;
            if (!string.IsNullOrEmpty(why)) Error(why);
        }

        // ------------------------------------------------------------ cancelling own buffs (right-click, WoW)

        /// <summary>True for a buff the player may right-click off (party unit's own removable buff: Ice Block, stealth, aspects…).</summary>
        public static bool CanCancel(AuraInstance a)
        {
            if (a == null || a.Def == null || a.IsDebuff || a.IsPassive || a.IsAreaChild || a.Def.hidden) return false;
            var u = a.Bearer;
            return u != null && u.Team == Team.Player && !u.OriginalTeam.HasValue && u.IsAlive;
        }

        /// <summary>
        /// Cancels one of a party unit's own buffs: Battle.CancelAura in combat, the session's field context outside
        /// (Battle.CancelAura is the rules' "right-click a buff"). The combat controller pulls the AuraRemoved event on its
        /// next tick. Run from Tick (posted), never from OnGUI.
        /// </summary>
        public static void CancelAura(Unit u, AuraInstance a)
        {
            if (u == null || a == null) return;
            try
            {
                Battle ctx = null;
                var b = Battle;
                if (b != null && !b.IsOver && b.Units.Contains(u)) ctx = b;
                else if (b == null)
                {
                    var s = Session;
                    var field = s != null ? s.Field : null;
                    if (field != null && field.Units.Contains(u)) ctx = field;
                }
                if (ctx == null) { Error("That cannot be cancelled right now."); return; }
                var r = ctx.CancelAura(u, a);
                if (!r.Ok) Error(string.IsNullOrEmpty(r.Reason) ? "That cannot be cancelled." : r.Reason);
                else Ui.Sfx?.Invoke("ui_close");
            }
            catch (Exception e) { LogOnce("cancelaura", "Cancelling an aura failed: " + e); Error("That cannot be cancelled right now."); }
        }

        // ------------------------------------------------------------ soul shards (warlock reagent, a bag item)
        public const string SoulShardItem = "soul_shard";
        static int shardFrame = -1, shardCount;

        /// <summary>Soul Shards in the party bags (cached per frame).</summary>
        public static int SoulShards
        {
            get
            {
                int f = Time.frameCount;
                if (f == shardFrame) return shardCount;
                shardFrame = f;
                shardCount = 0;
                try { var s = Session; if (s != null) shardCount = s.CountItem(SoulShardItem); }
                catch (Exception) { shardCount = 0; }
                return shardCount;
            }
        }

        /// <summary>Abilities that consume a Soul Shard (Soul Fire, Shadowburn, demon summons, stones, Enslave Demon).</summary>
        public static bool UsesSoulShard(AbilityDef a) =>
            a != null && (a.special == "WarlockConsumeSoulShard" || a.special == "WarlockEnslaveDemon");

        // ------------------------------------------------------------ posted commands (clicks → Tick)
        static readonly List<Action> commands = new List<Action>();
        static readonly List<Action> running = new List<Action>();

        /// <summary>Queues an action for the next Tick (never mutate game state from inside OnGUI).</summary>
        public static void Post(Action a) { if (a != null) commands.Add(a); }

        /// <summary>Runs posted commands (called once per frame by the action bar's Tick).</summary>
        public static void RunCommands()
        {
            if (commands.Count == 0) return;
            running.Clear();
            running.AddRange(commands);
            commands.Clear();
            for (int i = 0; i < running.Count; i++)
            {
                try { running[i](); }
                catch (Exception e) { Debug.LogException(e); }
            }
            running.Clear();
        }

        // ------------------------------------------------------------ errors (red lane, shown by ToastsHud)
        internal static readonly List<string> PendingErrors = new List<string>();
        public static void Error(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (PendingErrors.Count < 8) PendingErrors.Add(text);
        }

        // ------------------------------------------------------------ unit helpers
        public static string NameOf(Unit u)
        {
            if (u == null) return "";
            var f = Flow;
            try { return f != null ? f.NameOf(u) : (u.Name ?? ""); }
            catch (Exception) { return u.Name ?? ""; }
        }

        public static readonly Color PartyTeam = Ui.Hex("#8fd6c4");
        public static readonly Color EnemyTeam = Ui.Hex("#ff7a6b");
        public static readonly Color NeutralTeam = Ui.Hex("#f2d27a");
        public static readonly Color Muted = Ui.Hex("#b8acc9");
        public static readonly Color Xp = Ui.Hex("#b07fe8");
        public static readonly Color GoldText = Ui.Hex("#ffd27a");
        public static readonly Color Absorb = new Color(1f, 1f, 0.95f, 0.55f);
        public static readonly Color DebuffRed = Ui.Hex("#e0503f");
        public static readonly Color BuffFrame = Ui.Hex("#8a9ab8");

        // ------------------------------------------------------------ colour caches (Ui.Hex parses on every call)
        static readonly Dictionary<string, Color> hexCache = new Dictionary<string, Color>();
        static readonly Color[] schoolCols = new Color[16], classCols = new Color[16], qualityCols = new Color[16];
        static readonly bool[] schoolSet = new bool[16], classSet = new bool[16], qualitySet = new bool[16];

        /// <summary>Ui.Hex with a cache.</summary>
        public static Color C(string hex)
        {
            if (hex == null) return Color.white;
            if (hexCache.TryGetValue(hex, out var c)) return c;
            c = Ui.Hex(hex);
            hexCache[hex] = c;
            return c;
        }

        public static Color SchoolCol(School s)
        {
            int i = (int)s;
            if (i < 0 || i >= schoolCols.Length) return Ui.SchoolColor(s);
            if (!schoolSet[i]) { schoolCols[i] = Ui.SchoolColor(s); schoolSet[i] = true; }
            return schoolCols[i];
        }

        public static Color ClassCol(ClassId k)
        {
            int i = (int)k;
            if (i < 0 || i >= classCols.Length) return Ui.ClassColor(k);
            if (!classSet[i]) { classCols[i] = Ui.ClassColor(k); classSet[i] = true; }
            return classCols[i];
        }

        public static Color QualityCol(Quality q)
        {
            int i = (int)q;
            if (i < 0 || i >= qualityCols.Length) return Ui.QualityColor(q);
            if (!qualitySet[i]) { qualityCols[i] = Ui.QualityColor(q); qualitySet[i] = true; }
            return qualityCols[i];
        }

        public static Color TeamColor(Unit u)
        {
            if (u == null) return Muted;
            if (u.Team == Team.Player) return PartyTeam;
            if (u.Team == Team.Enemy) return EnemyTeam;
            return NeutralTeam;
        }

        /// <summary>Class colour for characters (also for their pets, slightly dimmed), team colour otherwise.</summary>
        public static Color UnitColor(Unit u)
        {
            if (u == null) return Muted;
            if (u.Class != null) return Hud.ClassCol(u.ClassId);
            if (u.Owner != null && u.Owner.Class != null && u.Team == u.Owner.Team) return Color.Lerp(Hud.ClassCol(u.Owner.ClassId), Color.white, 0.25f);
            return TeamColor(u);
        }

        public static Color HealthColor(float pct01)
        {
            if (pct01 >= 0.5f) return Ui.Health;
            if (pct01 >= 0.25f) return Color.Lerp(Hud.C("#e8b44d"), Ui.Health, (pct01 - 0.25f) / 0.25f);
            return Color.Lerp(Ui.HealthLow, Hud.C("#e8b44d"), pct01 / 0.25f);
        }

        public static float AbsorbOf(Unit u)
        {
            if (u == null) return 0f;
            float t = 0f;
            var list = u.Auras;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a != null && a.Def != null && a.Def.absorb != null && a.AbsorbLeft > 0f) t += a.AbsorbLeft;
            }
            return t;
        }

        /// <summary>Own or shared-group cooldown left (Unit.CooldownLeft caches its "grp:" key per AbilityDef: no allocation).</summary>
        public static float CooldownLeft(Unit u, AbilityDef a)
        {
            if (u == null || a == null) return 0f;
            try { return u.CooldownLeft(a); }
            catch (Exception) { return 0f; }
        }

        public static bool HasTag(AbilityDef a, string tag)
        {
            if (a == null || a.tags == null) return false;
            for (int i = 0; i < a.tags.Length; i++)
                if (string.Equals(a.tags[i], tag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Total seconds of a pending cast for cast bars: PendingCast.TotalTime (hasted, with pushback), at least its
        /// remaining time.</summary>
        public static float PendingTotal(PendingCast p)
        {
            if (p == null) return 1f;
            float total = p.TotalTime;
            if (total <= 1e-3f)
            {
                if (p.Channel && p.ChannelDuration > 0f) total = p.ChannelDuration;
                else if (p.Ability != null) total = p.Ability.castTime;
            }
            return Mathf.Max(total, p.RemainingTime, 0.1f);
        }

        /// <summary>Elapsed fraction of a pending cast (PendingCast.Progress; for cast bars).</summary>
        public static float PendingProgress(PendingCast p)
        {
            if (p == null) return 0f;
            if (p.TotalTime > 1e-3f) return p.Progress;
            return 1f - Mathf.Clamp01(p.RemainingTime / PendingTotal(p));
        }

        public static string RankName(CreatureRank r)
        {
            switch (r)
            {
                case CreatureRank.Elite: return "Elite";
                case CreatureRank.Rare: return "Rare";
                case CreatureRank.Boss: return "Boss";
                case CreatureRank.Minion: return "Minion";
                default: return "";
            }
        }

        // ------------------------------------------------------------ mouse occlusion between IMGUI layers
        // IMGUI hands a click to the FIRST control drawn under the mouse, i.e. to the lower screen. Screens above the
        // HUD register the rects they drew (last frame); a HUD layer whose point is covered sees the mouse "nowhere".
        // The panels' kit (Lanternvale.Game.Panels.PanelKit.CoveredAbove/Occlude) shares the same convention: HUD rects
        // are registered there too, and panel rects cover the HUD.
        struct Occ { public int Order; public Rect Rect; }
        static List<Occ> occNow = new List<Occ>(8), occLast = new List<Occ>(8);
        static int occRollFrame = -1;

        /// <summary>Rolls the HUD's own occluders (once per frame, from Tick).</summary>
        public static void RollOccluders()
        {
            int f = Time.frameCount;
            if (f == occRollFrame) return;
            occRollFrame = f;
            var t = occLast;
            occLast = occNow;
            occNow = t;
            occNow.Clear();
        }

        /// <summary>Registers a rect drawn by a HUD screen with this order (Repaint only), for lower layers and the panels.</summary>
        public static void Occlude(Rect r, int order)
        {
            var e = Event.current;
            if (e == null || e.type != EventType.Repaint) return;
            occNow.Add(new Occ { Order = order, Rect = r });
            Lanternvale.Game.Panels.PanelKit.Occlude(r, order);
        }

        /// <summary>True when the GUI point is covered by a screen drawn above `order` (last frame).</summary>
        public static bool CoveredAbove(int order, Vector2 guiPoint)
        {
            for (int i = 0; i < occLast.Count; i++)
                if (occLast[i].Order > order && occLast[i].Rect.Contains(guiPoint)) return true;
            return Lanternvale.Game.Panels.PanelKit.CoveredAbove(order, guiPoint);
        }

        // ------------------------------------------------------------ logging
        static readonly HashSet<string> warned = new HashSet<string>();
        public static void LogOnce(string key, string message)
        {
            if (!warned.Add(key ?? "")) return;
            Debug.LogWarning("[Lanternvale HUD] " + message);
        }

        // ------------------------------------------------------------ portraits
        public struct PortraitArt
        {
            public Texture2D Tex;
            public bool IsPortrait;   // true: a portrait_* bust; false: a full-body sprite (cropped)
        }

        static readonly Dictionary<Unit, PortraitArt> portraitCache = new Dictionary<Unit, PortraitArt>();
        static readonly Dictionary<string, PortraitArt> portraitKeyCache = new Dictionary<string, PortraitArt>();

        public static string SpriteKeyOf(Unit u)
        {
            if (u == null) return "";
            if (!string.IsNullOrEmpty(u.Sprite)) return u.Sprite;
            if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.sprite)) return u.Companion.sprite;
            if (u.Class != null && !string.IsNullOrEmpty(u.Class.sprite)) return u.Class.sprite;
            if (u.Creature != null && !string.IsNullOrEmpty(u.Creature.sprite)) return u.Creature.sprite;
            return "";
        }

        public static PortraitArt PortraitOf(Unit u)
        {
            if (u == null) return default;
            if (portraitCache.TryGetValue(u, out var p) && p.Tex != null) return p;
            if (portraitCache.Count > 128) portraitCache.Clear();
            p = ResolvePortrait(u);
            portraitCache[u] = p;
            return p;
        }

        static PortraitArt ResolvePortrait(Unit u)
        {
            string key = "";
            if (!string.IsNullOrEmpty(u.Portrait)) key = u.Portrait;
            else if (u.Companion != null && !string.IsNullOrEmpty(u.Companion.portrait)) key = u.Companion.portrait;
            else if (u.Class != null && u.Companion == null && !string.IsNullOrEmpty(u.Class.portrait)) key = u.Class.portrait;
            else if (u.Creature != null && !string.IsNullOrEmpty(u.Creature.portrait)) key = u.Creature.portrait;
            string sprite = SpriteKeyOf(u);
            if (string.IsNullOrEmpty(key) && sprite.Length > 0)
            {
                int us = sprite.IndexOf('_');
                if (us > 0 && us < sprite.Length - 1)
                {
                    var guess = "portrait_" + sprite.Substring(us + 1);
                    if (ArtLibrary.HasRealTexture(guess)) key = guess;
                }
            }
            if (string.IsNullOrEmpty(key) && u.Class != null && !string.IsNullOrEmpty(u.Class.portrait)) key = u.Class.portrait;
            return ArtFor(key, sprite);
        }

        /// <summary>Portrait art for a key (falls back to the sprite, cropped to the upper body).</summary>
        public static PortraitArt ArtFor(string portraitKey, string spriteKey)
        {
            string ck = (portraitKey ?? "") + "|" + (spriteKey ?? "");
            if (portraitKeyCache.TryGetValue(ck, out var p) && p.Tex != null) return p;
            p = default;
            try
            {
                if (!string.IsNullOrEmpty(portraitKey) && (ArtLibrary.HasRealTexture(portraitKey) || string.IsNullOrEmpty(spriteKey)))
                    p = new PortraitArt { Tex = ArtLibrary.Texture(portraitKey), IsPortrait = true };
                else if (!string.IsNullOrEmpty(spriteKey))
                    p = new PortraitArt { Tex = ArtLibrary.Texture(spriteKey), IsPortrait = false };
                else
                    p = new PortraitArt { Tex = ArtLibrary.Texture("portrait_unknown"), IsPortrait = true };
            }
            catch (Exception e) { LogOnce("portrait:" + ck, "Portrait art failed: " + e.Message); }
            portraitKeyCache[ck] = p;
            return p;
        }

        /// <summary>Forget cached per-unit data (new game, map change).</summary>
        public static void ClearUnitCaches()
        {
            portraitCache.Clear();
            HudText.ClearUnitCaches();
            RankPins.ClearUnitKeys();
            targetStates.Clear();
            targetStamp = -1;
        }
    }

    /// <summary>Layout of the bottom-centre HUD block (shared by the action bar, turn economy, log and errors).</summary>
    public static class HudLayout
    {
        public const int SlotsPerPage = 12;
        public const float Slot = 58f, Gap = 6f, Pad = 10f, Margin = 14f;
        public const float ItemSlot = 46f, ItemGap = 5f;

        public static float BarWidth => SlotsPerPage * Slot + (SlotsPerPage - 1) * Gap + 2f * Pad;
        public static float BarHeight => Slot + 2f * Pad;

        public static Rect BarPanel
        {
            get
            {
                float w = BarWidth, h = BarHeight;
                return new Rect(Mathf.Round(Ui.Width * 0.5f - w * 0.5f), Mathf.Round(Ui.Height - Margin - h), w, h);
            }
        }

        public static Rect SlotRect(int i)
        {
            var p = BarPanel;
            return new Rect(p.x + Pad + i * (Slot + Gap), p.y + Pad, Slot, Slot);
        }

        public static Rect Pager { get { var p = BarPanel; return new Rect(p.x - 36f, p.y + 4f, 30f, p.height - 8f); } }

        public static Rect EndTurn { get { var p = BarPanel; return new Rect(p.xMax + 12f, p.y + 6f, 178f, p.height - 12f); } }

        /// <summary>Right edge available for the consumables strip (left of the pager).</summary>
        public static float ConsumablesRight => Pager.x - 8f;

        public static Rect TurnRow { get { var p = BarPanel; return new Rect(Mathf.Round(Ui.Width * 0.5f - 340f), p.y - 10f - 46f, 680f, 46f); } }

        public static Rect StatusPill { get { var t = TurnRow; return new Rect(Mathf.Round(Ui.Width * 0.5f - 320f), t.y - 8f - 34f, 640f, 34f); } }

        public static float ErrorY => StatusPill.y - 44f;

        /// <summary>The menu buttons row (top right, under the clock/gold pill; the quest tracker sits below it).</summary>
        public static Rect MenuBar
        {
            get
            {
                float w = QuestTrackerHud.W;
                return new Rect(Ui.Width - Margin - w, Margin + QuestTrackerHud.ClockH + 6f, w, 38f);
            }
        }

        public static Rect CompactLog
        {
            get
            {
                var p = BarPanel;
                float w = Mathf.Min(470f, TurnRow.x - Margin - 12f);
                if (w < 260f) w = 260f;
                return new Rect(Margin, p.y - 12f - 112f, w, 112f);
            }
        }
    }

    /// <summary>Cached strings (numbers, seconds, compound labels) so the HUD does not allocate every frame.</summary>
    public static class HudText
    {
        static readonly string[] ints = new string[4096];
        static readonly string[] tenths = new string[100];
        static readonly string[] negTenths = new string[100];

        public static string Int(int n)
        {
            if (n >= 0 && n < ints.Length) return ints[n] ??= n.ToString(CultureInfo.InvariantCulture);
            return n.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"4.5" below 10 s, "12" above (ceil).</summary>
        public static string Secs(float s)
        {
            if (s < 0f) s = 0f;
            if (s < 9.95f)
            {
                int t = Mathf.Clamp(Mathf.RoundToInt(s * 10f), 0, 99);
                return tenths[t] ??= (t / 10).ToString(CultureInfo.InvariantCulture) + "." + (t % 10).ToString(CultureInfo.InvariantCulture);
            }
            return Int(Mathf.CeilToInt(s));
        }

        /// <summary>"−0.5" style (for debt).</summary>
        public static string NegSecs(float s)
        {
            int t = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(s) * 10f), 0, 99);
            return negTenths[t] ??= "-" + (t / 10).ToString(CultureInfo.InvariantCulture) + "." + (t % 10).ToString(CultureInfo.InvariantCulture);
        }

        static readonly string[] durS = new string[60], durM = new string[60], durH = new string[48];

        /// <summary>Compact duration for icons: "12", "3m", "2h".</summary>
        public static string Duration(float s)
        {
            if (s <= 0f) return "";
            if (s < 60f) { int i = Mathf.Clamp(Mathf.CeilToInt(s), 0, 59); return durS[i] ??= i.ToString(CultureInfo.InvariantCulture); }
            if (s < 3600f) { int m = Mathf.Clamp(Mathf.CeilToInt(s / 60f), 0, 59); return durM[m] ??= m.ToString(CultureInfo.InvariantCulture) + "m"; }
            int h = Mathf.Clamp(Mathf.CeilToInt(s / 3600f), 0, 47);
            return durH[h] ??= h.ToString(CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>A "a / b" label cached on the last values (one instance per drawn element).</summary>
        public sealed class Pair
        {
            int a = int.MinValue, b = int.MinValue;
            string s = "";
            readonly string sep;
            public Pair(string separator = " / ") { sep = separator; }
            public string Get(int x, int y)
            {
                if (x == a && y == b) return s;
                a = x; b = y;
                s = Int(x) + sep + Int(y);
                return s;
            }
        }

        /// <summary>A label cached on one int (prefix + n + suffix).</summary>
        public sealed class One
        {
            int a = int.MinValue;
            string s = "";
            readonly string pre, post;
            public One(string prefix, string suffix = "") { pre = prefix ?? ""; post = suffix ?? ""; }
            public string Get(int x)
            {
                if (x == a) return s;
                a = x;
                s = pre + Int(x) + post;
                return s;
            }
        }

        /// <summary>A label cached on one float rounded to tenths.</summary>
        public sealed class Tenths
        {
            int a = int.MinValue;
            string s = "";
            readonly string pre, post;
            public Tenths(string prefix, string suffix = "") { pre = prefix ?? ""; post = suffix ?? ""; }
            public string Get(float v)
            {
                int t = Mathf.RoundToInt(v * 10f);
                if (t == a) return s;
                a = t;
                s = pre + (v < 0 ? "-" : "") + (Mathf.Abs(t) / 10).ToString(CultureInfo.InvariantCulture) + "." + (Mathf.Abs(t) % 10).ToString(CultureInfo.InvariantCulture) + post;
                return s;
            }
        }

        // per-unit caches (health/resource labels)
        public sealed class UnitLabels
        {
            public readonly Pair Health = new Pair(), Resource = new Pair(), PetHealth = new Pair();
            public readonly One Level = new One("");
            public readonly One Rage = new One("");
            public int StampFrame;
        }

        static readonly Dictionary<Unit, UnitLabels> unitLabels = new Dictionary<Unit, UnitLabels>();

        public static UnitLabels For(Unit u)
        {
            if (u == null) return Shared;
            if (unitLabels.TryGetValue(u, out var l)) return l;
            if (unitLabels.Count > 96) unitLabels.Clear();
            l = new UnitLabels();
            unitLabels[u] = l;
            return l;
        }

        static readonly UnitLabels Shared = new UnitLabels();

        internal static void ClearUnitCaches() => unitLabels.Clear();

        public static string Strip(string t)
        {
            if (string.IsNullOrEmpty(t) || t.IndexOf('<') < 0) return t;
            var sb = new StringBuilder(t.Length);
            bool inTag = false;
            foreach (var ch in t)
            {
                if (ch == '<') { inTag = true; continue; }
                if (ch == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(ch);
            }
            return sb.ToString();
        }

        public static string ResourceName(ResourceType r)
        {
            switch (r)
            {
                case ResourceType.Mana: return "Mana";
                case ResourceType.Rage: return "Rage";
                case ResourceType.Energy: return "Energy";
                case ResourceType.Focus: return "Focus";
                default: return "";
            }
        }

        static readonly string[] hotkeys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };
        public static string Hotkey(int slot) => slot >= 0 && slot < hotkeys.Length ? hotkeys[slot] : "";

        static readonly string[] dots = { "", ".", "..", "..." };
        public static string Dots() => dots[(int)(Time.unscaledTime * 2.5f) % 4];
    }

    /// <summary>Cached GUIStyles for the HUD (built from the Ui toolkit fonts once they exist).</summary>
    public static class HudStyles
    {
        static GUIStyle builtFrom;
        static float builtFactor = -1f;
        /// <summary>Increments whenever the styles are rebuilt (font sizes may have changed): drop cached text measurements.</summary>
        public static int Version { get; private set; }
        public static GUIStyle Name, NameSmall, Small, SmallWrap, SmallRight, SmallCenter, Tiny, TinyCenter, TinyRight, Body, BodyCenter, BodyWrap,
            Header, TitleHuge, TitleBig, Subtitle, ToastText, LogWrap, LogLine, Button, Center, Label18, TitleLine;

        /// <summary>
        /// Builds the styles (again when the screen scale changes). The HUD draws in a virtual 1080p space, so a 15 px
        /// label is 10 physical px at 720p: the small styles (≤ 16 px) grow below 810p so they never render smaller than
        /// about 11 physical px.
        /// </summary>
        public static void Ensure()
        {
            if (Ui.Label == null) return;
            float factor = Mathf.Max(1f, 0.75f / Mathf.Max(0.1f, Ui.Scale));
            factor = Mathf.Ceil(factor * 20f) / 20f;   // steps of 5 %: no rebuild for every pixel of a window resize
            if (builtFrom == Ui.Label && Name != null && Mathf.Abs(factor - builtFactor) < 0.001f) return;
            builtFrom = Ui.Label;
            builtFactor = factor;
            Version++;
            Font body = Ui.BodyFont, bold = Ui.BoldFont, title = Ui.TitleFont;
            GUIStyle Make(Font f, int size, TextAnchor a, bool wrap = false, bool rich = true)
            {
                if (size <= 16) size = Mathf.CeilToInt(size * factor);
                var s = new GUIStyle(Ui.Label) { font = f, fontSize = size, alignment = a, wordWrap = wrap, richText = rich, clipping = TextClipping.Clip };
                s.padding = new RectOffset(0, 0, 0, 0);
                s.margin = new RectOffset(0, 0, 0, 0);
                s.normal.textColor = Ui.TextLight;
                return s;
            }
            Name = Make(bold, 17, TextAnchor.MiddleLeft);
            NameSmall = Make(bold, 16, TextAnchor.MiddleLeft);
            Small = Make(body, 16, TextAnchor.MiddleLeft);
            SmallWrap = Make(body, 16, TextAnchor.UpperLeft, true);   // Small that wraps (size with CalcHeight)
            SmallRight = Make(body, 16, TextAnchor.MiddleRight);
            SmallCenter = Make(body, 16, TextAnchor.MiddleCenter);
            // numbers read every turn (health, resource, costs, durations, levels): 15 px, never clipped vertically
            Tiny = Make(bold, 15, TextAnchor.MiddleLeft);
            TinyCenter = Make(bold, 15, TextAnchor.MiddleCenter);
            TinyRight = Make(bold, 15, TextAnchor.MiddleRight);
            Tiny.clipping = TinyCenter.clipping = TinyRight.clipping = TextClipping.Overflow;
            Body = Make(body, 16, TextAnchor.MiddleLeft);
            BodyCenter = Make(body, 16, TextAnchor.MiddleCenter);
            BodyWrap = Make(body, 15, TextAnchor.UpperLeft, true);
            Label18 = Make(bold, 18, TextAnchor.MiddleLeft);
            TitleLine = Make(bold, 16, TextAnchor.UpperLeft, true);
            Center = Make(bold, 18, TextAnchor.MiddleCenter);
            Header = Make(title, 20, TextAnchor.MiddleLeft);
            Header.normal.textColor = Ui.Gold;
            TitleHuge = Make(title, 66, TextAnchor.MiddleCenter);
            TitleBig = Make(title, 40, TextAnchor.MiddleCenter);
            Subtitle = Make(body, 21, TextAnchor.UpperCenter, true);
            ToastText = Make(bold, 18, TextAnchor.MiddleLeft);
            LogWrap = Make(body, 15, TextAnchor.UpperLeft, true);
            LogLine = Make(body, 15, TextAnchor.MiddleLeft);
            Button = new GUIStyle(Ui.ButtonDark) { fontSize = 15, padding = new RectOffset(8, 8, 4, 6) };
        }
    }

    /// <summary>Allocation-free drawing primitives (9-sliced rounded fills/rings, bars, icons, glyphs, portraits).</summary>
    public static class HudDraw
    {
        static GUIStyle fill3, fill5, fill8, fill12, ring5, ring8, ring12, ringThick8;
        static Texture2D[] sweep;
        const int SweepFrames = 40;

        public static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;
        public static Vector2 Mouse => Event.current != null ? Event.current.mousePosition : new Vector2(-9999, -9999);
        public static bool Hover(Rect r) => Event.current != null && r.Contains(Event.current.mousePosition);

        /// <summary>Left (or other) mouse button pressed inside r in this event. Does not consume the event.</summary>
        public static bool Click(Rect r, int button = 0)
        {
            var e = Event.current;
            return e != null && e.type == EventType.MouseDown && e.button == button && r.Contains(e.mousePosition);
        }

        static GUIStyle Slice(int texSize, int radius, Color fill, Color border, int bw)
        {
            var t = ProceduralArt.RoundedRect(texSize, radius, fill, border, bw);
            int b = radius + 1;
            return new GUIStyle { normal = { background = t }, border = new RectOffset(b, b, b, b) };
        }

        static void Build()
        {
            if (fill8 != null && fill8.normal.background != null) return;
            var clear = new Color(1f, 1f, 1f, 0f);
            fill3 = Slice(16, 3, Color.white, Color.white, 0);
            fill5 = Slice(24, 5, Color.white, Color.white, 0);
            fill8 = Slice(32, 8, Color.white, Color.white, 0);
            fill12 = Slice(48, 12, Color.white, Color.white, 0);
            ring5 = Slice(24, 5, clear, Color.white, 2);
            ring8 = Slice(32, 8, clear, Color.white, 2);
            ring12 = Slice(48, 12, clear, Color.white, 2);
            ringThick8 = Slice(32, 8, clear, Color.white, 3);
        }

        static GUIStyle FillFor(int radius)
        {
            Build();
            if (radius <= 3) return fill3;
            if (radius <= 5) return fill5;
            if (radius <= 8) return fill8;
            return fill12;
        }

        /// <summary>Rounded filled rectangle (radius 3/5/8/12), tinted.</summary>
        public static void Fill(Rect r, Color c, int radius = 5)
        {
            if (!IsRepaint || r.width <= 0.5f || r.height <= 0.5f) return;
            if (r.width < 8f || r.height < 8f) { Solid(r, c); return; }   // too small for the 9-slice borders
            var st = FillFor(Mathf.Min(radius, (int)(Mathf.Min(r.width, r.height) * 0.5f)));
            var old = GUI.color;
            GUI.color = c;
            st.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>Rounded outline, tinted. thick = 3 px instead of 2.</summary>
        public static void Ring(Rect r, Color c, int radius = 8, bool thick = false)
        {
            if (!IsRepaint || r.width <= 1f || r.height <= 1f) return;
            Build();
            GUIStyle st;
            if (thick) st = ringThick8;
            else if (radius <= 5) st = ring5;
            else if (radius <= 8) st = ring8;
            else st = ring12;
            var old = GUI.color;
            GUI.color = c;
            st.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = old;
        }

        public static void Solid(Rect r, Color c)
        {
            if (!IsRepaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, ProceduralArt.White);
            GUI.color = old;
        }

        public static void Glow(Rect r, Color c)
        {
            if (!IsRepaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, ProceduralArt.Glow);
            GUI.color = old;
        }

        /// <summary>Panel background used by HUD frames: translucent night ink with a faint gold edge.</summary>
        public static void Frame(Rect r, float alpha = 0.82f, Color? edge = null)
        {
            Fill(new Rect(r.x + 3f, r.y + 4f, r.width, r.height), new Color(0f, 0f, 0f, 0.18f * alpha), 8);
            Fill(r, new Color(0.11f, 0.09f, 0.18f, alpha), 8);
            Ring(r, edge ?? new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.45f * alpha), 8);
        }

        /// <summary>Horizontal bar: dark back, coloured fill (0..1), soft top highlight.</summary>
        public static void Bar(Rect r, float fill, Color col, float backAlpha = 0.8f)
        {
            if (!IsRepaint) return;
            int rad = r.height >= 14f ? 5 : 3;
            Fill(r, new Color(0.04f, 0.03f, 0.08f, backAlpha), rad);
            fill = Mathf.Clamp01(fill);
            if (fill <= 0.002f) return;
            var fr = new Rect(r.x + 1.5f, r.y + 1.5f, (r.width - 3f) * fill, r.height - 3f);
            Fill(fr, col, rad);
            Solid(new Rect(fr.x + 1f, fr.y + 1f, Mathf.Max(0f, fr.width - 2f), Mathf.Max(1f, fr.height * 0.38f)), new Color(1f, 1f, 1f, 0.16f));
        }

        /// <summary>A segment of a bar (overlay) between two fractions.</summary>
        public static void BarSegment(Rect r, float from01, float to01, Color col)
        {
            if (!IsRepaint) return;
            from01 = Mathf.Clamp01(from01);
            to01 = Mathf.Clamp01(to01);
            if (to01 <= from01) return;
            float w = r.width - 3f;
            Solid(new Rect(r.x + 1.5f + w * from01, r.y + 1.5f, w * (to01 - from01), r.height - 3f), col);
        }

        /// <summary>Text with a soft dark shadow (plain text; rich text colours would leak into the shadow).</summary>
        public static void Text(Rect r, string text, GUIStyle st, Color c, bool shadow = true)
        {
            if (string.IsNullOrEmpty(text) || st == null) return;
            if (Event.current.type != EventType.Repaint) return;
            var orig = st.normal.textColor;
            if (shadow)
            {
                st.normal.textColor = new Color(0f, 0f, 0f, 0.8f * c.a);
                GUI.Label(new Rect(r.x + 1f, r.y + 1.5f, r.width, r.height), text, st);
            }
            st.normal.textColor = c;
            GUI.Label(r, text, st);
            st.normal.textColor = orig;
        }

        /// <summary>Rich text (no shadow unless a plain copy is given).</summary>
        public static void Rich(Rect r, string rich, GUIStyle st, string plainShadow = null, float alpha = 1f)
        {
            if (string.IsNullOrEmpty(rich) || st == null) return;
            if (Event.current.type != EventType.Repaint) return;
            var orig = st.normal.textColor;
            var oc = GUI.color;
            GUI.color = new Color(oc.r, oc.g, oc.b, oc.a * alpha);
            if (!string.IsNullOrEmpty(plainShadow))
            {
                st.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1.5f, r.width, r.height), plainShadow, st);
            }
            st.normal.textColor = Ui.TextLight;
            GUI.Label(r, rich, st);
            st.normal.textColor = orig;
            GUI.color = oc;
        }

        /// <summary>White glyph (Art "glyph_*") tinted; falls back to the first letter.</summary>
        public static void Glyph(Rect r, string glyph, Color c)
        {
            if (!IsRepaint) return;
            var tex = Ui.GlyphTexture(glyph);
            var old = GUI.color;
            if (tex != null)
            {
                GUI.color = c;
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
                GUI.color = old;
                return;
            }
            GUI.color = old;
            Text(r, Letter(glyph), Ui.NumberStyle(Mathf.Clamp((int)(r.height * 0.62f), 9, 40)), c);
        }

        static readonly Dictionary<string, string> letters = new Dictionary<string, string>();
        static string Letter(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return "?";
            if (letters.TryGetValue(glyph, out var s)) return s;
            string g = glyph.StartsWith("glyph_") ? glyph.Substring(6) : glyph;
            s = g.Length > 0 ? g.Substring(0, 1).ToUpperInvariant() : "?";
            letters[glyph] = s;
            return s;
        }

        /// <summary>Ability/item icon: dark school-tinted tile, glyph, coloured rounded frame, optional dim.</summary>
        public static void Icon(Rect r, string glyph, Color frame, bool dim = false, float alpha = 1f)
        {
            if (!IsRepaint) return;
            var back = Color.Lerp(frame, Color.black, 0.62f);
            back.a = 0.95f * alpha;
            Fill(r, back, r.width >= 40f ? 8 : 5);
            // soft inner light so glyphs read like painted icons
            Glow(new Rect(r.x + r.width * 0.05f, r.y + r.height * 0.02f, r.width * 0.9f, r.height * 0.9f), new Color(frame.r, frame.g, frame.b, 0.22f * alpha));
            float inset = r.width * 0.15f;
            var inner = new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f);
            Glyph(inner, glyph, new Color(1f, 0.98f, 0.92f, (dim ? 0.42f : 1f) * alpha));
            var fc = frame;
            fc.a = (dim ? 0.55f : 0.95f) * alpha;
            Ring(r, fc, r.width >= 40f ? 8 : 5);
            if (dim) Fill(r, new Color(0.02f, 0.01f, 0.05f, 0.42f * alpha), r.width >= 40f ? 8 : 5);
        }

        /// <summary>Clockwise cooldown sweep: remaining01 = 1 just used … 0 ready.</summary>
        public static void Sweep(Rect r, float remaining01)
        {
            if (!IsRepaint || remaining01 <= 0.001f) return;
            EnsureSweep();
            int k = Mathf.Clamp(Mathf.CeilToInt(remaining01 * SweepFrames), 1, SweepFrames);
            var tex = sweep[k];
            if (tex == null) return;
            var old = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        static void EnsureSweep()
        {
            if (sweep != null && sweep[SweepFrames] != null) return;
            sweep = new Texture2D[SweepFrames + 1];
            const int n = 48;
            const float radius = 10f;
            var px = new Color32[n * n];
            for (int k = 1; k <= SweepFrames; k++)
            {
                float f = (float)k / SweepFrames;
                float limit = (1f - f) * Mathf.PI * 2f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                        float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                        float inside = Mathf.Clamp01(radius - d + 0.5f);
                        float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                        float ang = Mathf.Atan2(dx, dy);
                        if (ang < 0f) ang += Mathf.PI * 2f;
                        float a = ang >= limit ? 0.66f : 0f;
                        // a faint bright edge along the sweep line
                        if (ang >= limit && ang - limit < 0.06f) a = 0.3f;
                        px[y * n + x] = new Color32(6, 4, 14, (byte)(255f * a * inside));
                    }
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "hud_sweep_" + k, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                t.SetPixels32(px);
                t.Apply(false, true);
                sweep[k] = t;
            }
        }

        /// <summary>Unit portrait in a rounded frame: portrait_* busts, or the upper part of the unit's sprite.</summary>
        public static void Portrait(Rect r, Unit u, Color frame, bool dim = false, float alpha = 1f)
        {
            if (!IsRepaint) return;
            DrawPortraitArt(r, Hud.PortraitOf(u), frame, dim, alpha);
        }

        public static void DrawPortraitArt(Rect r, Hud.PortraitArt art, Color frame, bool dim, float alpha = 1f)
        {
            if (!IsRepaint) return;
            int rad = r.width >= 48f ? 8 : 5;
            Fill(r, new Color(0.10f, 0.08f, 0.16f, 0.92f * alpha), rad);
            var inner = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f);
            var tex = art.Tex;
            if (tex != null)
            {
                var old = GUI.color;
                GUI.color = dim ? new Color(0.45f, 0.45f, 0.52f, alpha) : new Color(1f, 1f, 1f, alpha);
                float R = inner.width / Mathf.Max(1f, inner.height);
                float tw = tex.width, th = tex.height;
                if (art.IsPortrait)
                {
                    // ScaleAndCrop keeping the top of the bust
                    float w = 1f, h = 1f;
                    float texR = tw / Mathf.Max(1f, th);
                    if (texR > R) w = R / texR; else h = texR / R;
                    GUI.DrawTextureWithTexCoords(inner, tex, new Rect((1f - w) * 0.5f, 1f - h, w, h), true);
                }
                else if (tw / Mathf.Max(1f, th) >= 1.15f)
                {
                    GUI.DrawTexture(inner, tex, ScaleMode.ScaleToFit, true);
                }
                else
                {
                    // bust crop of a full-body sprite: the top ~58% (heads sit near the top)
                    float hPx = th * 0.58f, wPx = hPx * R;
                    if (wPx > tw) { wPx = tw; hPx = wPx / R; }
                    float u0 = (tw - wPx) * 0.5f / tw, v1 = 0.985f, vh = hPx / th;
                    GUI.DrawTextureWithTexCoords(inner, tex, new Rect(u0, Mathf.Max(0f, v1 - vh), wPx / tw, vh), true);
                }
                GUI.color = old;
            }
            var fc = frame;
            fc.a *= alpha;
            Ring(r, fc, rad, r.width >= 56f);
        }

        /// <summary>Small round pip (combo points, totem markers).</summary>
        public static void Pip(Rect r, Color c, bool filled)
        {
            if (!IsRepaint) return;
            Fill(r, new Color(0.03f, 0.02f, 0.06f, 0.85f), 5);
            if (filled) Fill(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), c, 5);
            Ring(r, new Color(c.r, c.g, c.b, filled ? 0.9f : 0.4f), 5);
        }

        static Texture2D arrowUp, arrowDown;

        /// <summary>Small filled triangle pointing up or down.</summary>
        public static void Arrow(Rect r, bool up, Color c)
        {
            if (!IsRepaint) return;
            if (arrowUp == null || arrowDown == null) { arrowUp = MakeArrow(true); arrowDown = MakeArrow(false); }
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, up ? arrowUp : arrowDown, ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        static Texture2D MakeArrow(bool up)
        {
            const int w = 32, h = 24;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // texture rows go bottom → top: an "up" arrow is wide at the bottom
                    float t = up ? (y + 0.5f) / h : 1f - (y + 0.5f) / h;   // 0 at the base, 1 at the tip
                    float half = (1f - t) * (w * 0.5f - 1f);
                    float d = half - Mathf.Abs(x + 0.5f - w * 0.5f);
                    byte a = (byte)(255f * Mathf.Clamp01(d + 0.5f));
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = up ? "hud_arrow_up" : "hud_arrow_down", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        public struct Layer
        {
            internal bool Hidden;
            internal Vector2 Saved;
            /// <summary>True while a higher screen covers the mouse (the layer sees it "nowhere").</summary>
            public bool IsHidden => Hidden;
            /// <summary>The real mouse position (GUI space at BeginLayer), also while hidden — for drags that cross other windows.</summary>
            public Vector2 RealMouse => Hidden ? Saved : (Event.current != null ? Event.current.mousePosition : Nowhere);
        }

        static readonly Vector2 Nowhere = new Vector2(-100000f, -100000f);
        static int hiddenLayers, hiddenFrame = -1;

        /// <summary>Hides the mouse from a HUD screen while a higher screen covers it. Always pair with EndLayer.</summary>
        public static Layer BeginLayer(int order)
        {
            var h = new Layer();
            var e = Event.current;
            if (e == null) return h;
            if (Hud.CoveredAbove(order, e.mousePosition))
            {
                h.Saved = e.mousePosition;
                h.Hidden = true;
                e.mousePosition = Nowhere;
                if (hiddenFrame != Time.frameCount) { hiddenFrame = Time.frameCount; hiddenLayers = 0; }   // never leak across frames
                hiddenLayers++;
            }
            return h;
        }

        public static void EndLayer(Layer h)
        {
            if (!h.Hidden) return;
            hiddenLayers = Mathf.Max(0, hiddenLayers - 1);
            var e = Event.current;
            if (e != null) e.mousePosition = h.Saved;
        }

        /// <summary>
        /// Re-applies an active layer hide. IMGUI recomputes Event.mousePosition from the real cursor whenever a clip is
        /// pushed/popped (GUI.BeginScrollView/EndScrollView, BeginGroup…) or GUI.matrix changes, which would let a covered
        /// HUD layer see the mouse again: call this right after each of those inside a layer.
        /// </summary>
        public static void Rehide()
        {
            var e = Event.current;
            if (e != null && hiddenLayers > 0 && hiddenFrame == Time.frameCount) e.mousePosition = Nowhere;
        }

        public static float Pulse(float speed = 3f, float min = 0.55f, float max = 1f) =>
            Mathf.Lerp(min, max, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed));
    }
}
