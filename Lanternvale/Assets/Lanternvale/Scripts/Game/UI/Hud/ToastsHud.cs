// Toasts, errors and banners.
//  * Toasts — THE single toast lane (centre top, queued, fading, merged): SessionEvents — quests (started/updated/
//    completed/failed/reward), items received (quality colour, stacked counts), money, experience, abilities learned,
//    talent points, companions, approval, skill-check rolls ("Persuasion check: 14 + 2 = 16 vs DC 12 — Success"), locked
//    transitions, rests, time of day, combat barks, telegraphed pending casts (CombatEventPresented), flow toasts and the
//    panels' notices (PanelKit.Notice → GameFlow.Toast(text, colour): red refusals, gold confirmations). The lane is
//    drawn by ToastLaneHud (Order 480) so it shows above windows and menus too; it never reaches down into the error lane.
//  * Error lane (red, above the bottom HUD): Combat.LastError ("Not enough rage (15).") and HUD failures.
//  * Banners (one at a time, kept below the toast stack): map title cards (Title font), region names, a short level-up
//    banner (the details are the panels' level-up card, bottom right), Victory!/Defeat/Disengaged (hooked to
//    Combat.Battle.IsOver — the world only gets sparkles and sound), combat start, story moments (SpecialOutcome), hidden
//    passages found (SecretFound, a gold story banner). A raid wipe is ONE notice: "The raid has wiped" takes the Defeat
//    banner's place, and the session's follow-ups (the return map's title card, RaidEnded, PartyHealed) only fill in its
//    second line ("You come to in Mirefen, your wounds tended.").
using System;
using System.Collections.Generic;
using System.Globalization;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class ToastsHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderToasts;
        public bool Visible => Hud.ToastsVisible;
        public bool Modal => false;

        const int MaxVisible = 5;
        const float FadeIn = 0.22f, FadeOut = 0.55f, ToastH = 34f;

        enum Kind { Info, Quest, QuestDone, Item, Money, Xp, Ability, Talent, Party, Approval, Check, Bark, Warn, Time, Notice }

        sealed class Toast
        {
            public Kind Kind;
            public string Text = "", Plain = "";
            public string Glyph;
            public Color Accent;
            public float Age, Life;
            public float Width = -1f;
            public string Key = "";
            public int Amount, Count;
            public Unit Unit;
            public string Name = "";
            public Color NameColor;
            public float Scale = 1f;
        }

        enum BannerKind { MapTitle, Region, LevelUp, Victory, Defeat, Left, Combat, Story }

        sealed class Banner
        {
            public BannerKind Kind;
            public string Title = "", Sub = "";
            public Color Color;
            public float Age, Life;
            public int Level;
            public readonly List<string> Names = new List<string>();
        }

        sealed class ErrorLine
        {
            public string Text;
            public float Age;
            public int Count = 1;
        }

        /// <summary>The instance UiRoot created (ToastLaneHud draws its lane).</summary>
        internal static ToastsHud Current { get; private set; }

        public ToastsHud() { Current = this; }

        /// <summary>Toasts on screen (the lane screen is visible while there are any).</summary>
        internal bool HasToasts => active.Count > 0;

        // how many toasts fit between the lane's top and the error lane (measured by the last lane draw)
        int laneFit = MaxVisible;
        float laneBottom = 120f;
        // last level-up per unit (unscaled time): its talent-point / learned toasts are the level-up card's job
        readonly Dictionary<Unit, float> levelUpAt = new Dictionary<Unit, float>();

        readonly List<Toast> active = new List<Toast>();
        readonly List<Toast> waiting = new List<Toast>();
        readonly List<Banner> banners = new List<Banner>();
        readonly List<ErrorLine> errors = new List<ErrorLine>();
        readonly GUIContent measure = new GUIContent();

        GameFlow subscribedFlow;
        CombatController errCombat;
        float errSeen;
        Battle bannerBattle;
        // the raid wipe being announced (its banner is the one notice; see ShowRaidWipe), and when it began
        Banner wipeBanner;
        float wipeAt = -100f;
        const float WipeWindow = 8f;

        static readonly Color QuestGold = Ui.Hex("#ffd27a");
        static readonly Color InfoCol = Ui.Hex("#e8e0f4");
        static readonly Color WarnCol = Ui.Hex("#ff9a7a");
        static readonly Color MoneyCol = Ui.Hex("#ffd75e");
        static readonly Color CheckGood = Ui.Hex("#7fd47a");
        static readonly Color CheckBad = Ui.Hex("#ff7a6b");
        static readonly Color SecretGold = new Color(1f, 0.82f, 0.36f);

        // ================================================================ tick

        public void Tick(float dt)
        {
            try
            {
                Subscribe();
                PollCombat();
                for (int i = 0; i < Hud.PendingErrors.Count; i++) AddError(Hud.PendingErrors[i]);
                Hud.PendingErrors.Clear();
                Age(dt);
            }
            catch (Exception e) { Hud.LogOnce("toast-tick:" + e.GetType().Name, "Toasts: " + e); }
        }

        void Subscribe()
        {
            var f = GameFlow.Instance;
            if (f == subscribedFlow) return;
            if (subscribedFlow != null)
            {
                subscribedFlow.SessionEventRaised -= OnSessionEvent;
                subscribedFlow.CombatEventPresented -= OnCombatEvent;
            }
            subscribedFlow = f;
            if (f != null)
            {
                f.SessionEventRaised += OnSessionEvent;
                f.CombatEventPresented += OnCombatEvent;
            }
        }

        void PollCombat()
        {
            var c = Hud.Combat;
            if (c != errCombat)
            {
                errCombat = c;
                errSeen = c != null ? c.LastErrorTime : 0f;
            }
            if (c == null) return;
            if (c.LastErrorTime > errSeen + 0.0001f)
            {
                errSeen = c.LastErrorTime;
                AddError(c.LastError);
            }
            // Victory/Defeat banners follow the presentation: the BattleEnd event is announced through
            // CombatEventPresented once the last blows have been animated (CombatEnded is the fallback).
        }

        void ShowOutcome(BattleOutcome o, Battle b)
        {
            switch (o)
            {
                case BattleOutcome.Victory:
                {
                    int n = b != null ? b.KilledCreatures.Count : 0;
                    AddBanner(new Banner { Kind = BannerKind.Victory, Title = "Victory!", Sub = n > 0 ? (n == 1 ? "1 foe defeated" : n + " foes defeated") : "", Color = Ui.Gold, Life = 2.8f });
                    break;
                }
                case BattleOutcome.Defeat:
                    if (RaidWipeAhead()) { ShowRaidWipe(); break; }
                    AddBanner(new Banner { Kind = BannerKind.Defeat, Title = "Defeat", Sub = "The party has fallen…", Color = Hud.C("#e8a0b0"), Life = 3.2f });
                    break;
                case BattleOutcome.Fled:
                    AddBanner(new Banner { Kind = BannerKind.Left, Title = "Disengaged", Sub = "", Color = Hud.Muted, Life = 1.8f });
                    break;
            }
        }

        // ================================================================ raid wipe (one notice)

        /// <summary>A defeat on the current map is a raid wipe that sends the party home (no game over).</summary>
        static bool RaidWipeAhead()
        {
            var s = Hud.Session;
            return s != null && RaidPlanning.WipeSendsHome(s.Db, s.MapDef);
        }

        /// <summary>True while the wipe's banner is up and the session's follow-up events are still arriving.</summary>
        bool Wiping => wipeBanner != null && banners.Contains(wipeBanner) && Time.unscaledTime - wipeAt < WipeWindow;

        void ShowRaidWipe()
        {
            if (Wiping) return;
            banners.RemoveAll(b => b.Kind == BannerKind.Combat || b.Kind == BannerKind.Region || b.Kind == BannerKind.MapTitle || b.Kind == BannerKind.Defeat);
            wipeBanner = new Banner { Kind = BannerKind.Defeat, Title = "The raid has wiped", Sub = "The party falls back to regroup…", Color = Hud.C("#e8a0b0"), Life = 6f };
            wipeAt = Time.unscaledTime;
            if (banners.Count >= 6) banners.RemoveAt(banners.Count - 1);
            banners.Insert(0, wipeBanner);
        }

        void Age(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var t = active[i];
                // when many toasts wait, the oldest hurry along
                float speed = waiting.Count > 0 && i == 0 ? 2f : 1f;
                t.Age += dt * speed;
                if (t.Age >= t.Life) active.RemoveAt(i);
            }
            while (active.Count < Mathf.Min(MaxVisible, laneFit) && waiting.Count > 0)
            {
                active.Add(waiting[0]);
                waiting.RemoveAt(0);
            }
            if (banners.Count > 0)
            {
                var b = banners[0];
                // title cards wait for a conversation (e.g. the opening scene) to end before they play
                bool waitsForDialogue = Hud.InDialogue && (b.Kind == BannerKind.Region || b.Kind == BannerKind.MapTitle);
                if (!waitsForDialogue) b.Age += dt * (banners.Count > 2 ? 1.6f : 1f);
                if (b.Age >= b.Life) banners.RemoveAt(0);
            }
            for (int i = errors.Count - 1; i >= 0; i--)
            {
                errors[i].Age += dt;
                if (errors[i].Age >= 2.9f) errors.RemoveAt(i);
            }
        }

        // ================================================================ intake

        void OnSessionEvent(SessionEvent e)
        {
            if (e == null) return;
            try { Handle(e); }
            catch (Exception ex) { Hud.LogOnce("toast-ev:" + e.Kind, "Toast for " + e.Kind + " failed: " + ex.Message); }
        }

        void Handle(SessionEvent e)
        {
            switch (e.Kind)
            {
                case SessionEventKind.Toast:
                {
                    if (string.IsNullOrEmpty(e.Text)) break;
                    // GameFlow.Toast(text, colour): panel notices (red refusals, gold confirmations) and coloured flow toasts
                    var col = Hud.Flow != null ? Hud.Flow.ToastColorOf(e) : null;
                    if (col.HasValue) Add(Kind.Notice, e.Text, null, col.Value, "note:" + e.Text);
                    else Add(Kind.Info, e.Text, null, InfoCol, "info:" + e.Text);
                    break;
                }
                case SessionEventKind.GameStarted:
                case SessionEventKind.GameLoaded:
                    ClearAll();
                    Hud.ClearUnitCaches();
                    break;
                case SessionEventKind.GameOver:
                    ClearAll();
                    break;
                case SessionEventKind.MapEntered:
                {
                    Hud.ClearUnitCaches();
                    if (Wiping) break;   // the wipe's banner says where the party comes to (PartyHealed)
                    var s = Hud.Session;
                    string sub = s != null && s.MapDef != null ? s.MapDef.subtitle ?? "" : "";
                    if (!string.IsNullOrEmpty(e.Text))
                    {
                        banners.RemoveAll(b => b.Kind == BannerKind.MapTitle || b.Kind == BannerKind.Region);
                        AddBanner(new Banner { Kind = BannerKind.MapTitle, Title = e.Text, Sub = sub, Color = Hud.C("#fff3d6"), Life = 4.6f });
                    }
                    break;
                }
                case SessionEventKind.RegionEntered:
                {
                    if (string.IsNullOrEmpty(e.Text)) break;
                    SplitRegion(e.Text, out var title, out var sub);
                    AddBanner(new Banner { Kind = BannerKind.Region, Title = title, Sub = sub, Color = Hud.C("#fff3d6"), Life = 4.4f });
                    break;
                }
                case SessionEventKind.TransitionLocked:
                    Add(Kind.Warn, string.IsNullOrEmpty(e.Text) ? "The way is shut." : e.Text, "glyph_lock", WarnCol, "lock:" + e.Id);
                    break;
                case SessionEventKind.SkillCheck:
                    AddCheck(e);
                    break;
                case SessionEventKind.QuestStarted:
                    Add(Kind.Quest, "New quest: " + QuestName(e), "glyph_star", QuestGold, "qs:" + e.Id);
                    QuestTrackerHud.Flash(e.Id);
                    break;
                case SessionEventKind.QuestUpdated:
                {
                    string qn = QuestName(e);
                    string txt = e.Quest != null && !string.IsNullOrEmpty(e.Quest.Text) ? e.Quest.Text : e.Text;
                    if (string.IsNullOrEmpty(txt)) break;
                    var t = Add(Kind.Quest, Ui.Rich(qn, QuestGold) + "  " + txt, "glyph_star", QuestGold, "qu:" + e.Id + ":" + txt, qn + "  " + txt);
                    if (t != null) t.Scale = 0.95f;
                    Ui.Sfx?.Invoke("ui_click");
                    QuestTrackerHud.Flash(e.Id);
                    break;
                }
                case SessionEventKind.QuestCompleted:
                {
                    var t = Add(Kind.QuestDone, "Quest complete: " + QuestName(e), "glyph_star", QuestGold, "qc:" + e.Id);
                    if (t != null) { t.Life += 1.5f; t.Scale = 1.1f; }
                    QuestTrackerHud.Flash(e.Id);
                    break;
                }
                case SessionEventKind.QuestFailed:
                    Add(Kind.Warn, "Quest failed: " + QuestName(e), "glyph_skull", WarnCol, "qf:" + e.Id);
                    break;
                case SessionEventKind.QuestRewardChoice:
                    Add(Kind.Quest, (string.IsNullOrEmpty(e.Text) ? "Choose your reward" : e.Text), "glyph_star", QuestGold, "qr:" + e.Id);
                    break;
                case SessionEventKind.ItemReceived:
                    AddItem(e, true);
                    break;
                case SessionEventKind.ItemLost:
                    AddItem(e, false);
                    break;
                case SessionEventKind.GoldChanged:
                    AddMoney(e.Amount);
                    break;
                case SessionEventKind.XpGained:
                    AddXp(e.Amount);
                    break;
                case SessionEventKind.LevelUp:
                    AddLevelUp(e);
                    if (e.Unit != null) levelUpAt[e.Unit] = Time.unscaledTime;
                    break;
                case SessionEventKind.AbilityLearned:
                    if (FromLevelUp(e.Unit)) break;   // the level-up card lists what a companion learned
                    AddLearned(e);
                    break;
                case SessionEventKind.TalentPointsAvailable:
                {
                    if (e.Amount <= 0 || FromLevelUp(e.Unit)) break;   // the level-up card offers "Talents (N)"
                    string who = Hud.NameOf(e.Unit);
                    string txt = !string.IsNullOrEmpty(e.Text) ? e.Text : who + " has " + e.Amount + " unspent talent point" + (e.Amount == 1 ? "" : "s") + ".";
                    var t = Add(Kind.Talent, txt + "  " + Ui.Rich("(N)", Hud.Muted), "glyph_sparkle", Hud.C("#c9a3f0"), "tp:" + who, txt + "  (N)");
                    if (t != null) { t.Text = txt + "  " + Ui.Rich("(N)", Hud.Muted); t.Plain = txt + "  (N)"; t.Width = -1f; }
                    break;
                }
                case SessionEventKind.CompanionRecruited:
                    Add(Kind.Party, string.IsNullOrEmpty(e.Text) ? Hud.NameOf(e.Unit) + " joins the party." : e.Text, "glyph_heart", Hud.PartyTeam, "rec:" + e.Id);
                    break;
                case SessionEventKind.CompanionDismissed:
                    Add(Kind.Party, Hud.NameOf(e.Unit) + " leaves the party.", "glyph_heart", Hud.Muted, "dis:" + e.Id);
                    break;
                case SessionEventKind.ApprovalChanged:
                    if (!string.IsNullOrEmpty(e.Text))
                        Add(Kind.Approval, e.Text, "glyph_heart", e.Amount >= 0 ? CheckGood : CheckBad, "ap:" + e.Id + ":" + e.Amount);
                    break;
                case SessionEventKind.Rested:
                case SessionEventKind.PartyHealed:
                    if (e.Kind == SessionEventKind.PartyHealed && Wiping)
                    {
                        // the raid wipe's second line, not a toast of its own
                        string detail = RaidPlanning.WipeDetail(e.Text);
                        if (!string.IsNullOrEmpty(detail)) { wipeBanner.Sub = detail; wipeBanner.Age = Mathf.Min(wipeBanner.Age, 1f); }
                        wipeBanner = null;
                        break;
                    }
                    if (!string.IsNullOrEmpty(e.Text)) Add(Kind.Info, e.Text, "glyph_moon", InfoCol, "rest:" + e.Text);
                    break;
                case SessionEventKind.RaidEnded:
                    // a wipe whose BattleEnd was never presented (a fight resolved without the presenter): the banner still comes
                    if (e.Amount == 1) ShowRaidWipe();
                    break;
                case SessionEventKind.SecretFound:
                    // "You discovered a hidden passage: The Root Hollows" (GameFlow no longer toasts it: this is the one notice)
                    if (!string.IsNullOrEmpty(e.Text))
                        AddBanner(new Banner { Kind = BannerKind.Story, Title = e.Text, Color = SecretGold, Life = 5f });
                    break;
                case SessionEventKind.CombatStarted:
                {
                    var bark = e.Text ?? "";
                    banners.RemoveAll(b => b.Kind == BannerKind.Region || b.Kind == BannerKind.MapTitle);
                    AddBanner(new Banner { Kind = BannerKind.Combat, Title = "Combat!", Sub = bark.Length > 0 ? "“" + bark.Trim('"', '“', '”') + "”" : "", Color = Hud.C("#ffb07a"), Life = 1.9f });
                    break;
                }
                case SessionEventKind.CombatEnded:
                    if (e.Battle != null && bannerBattle != e.Battle)
                    {
                        bannerBattle = e.Battle;
                        if (e.Outcome == CombatEndKind.Victory) ShowOutcome(BattleOutcome.Victory, e.Battle);
                        else if (e.Outcome == CombatEndKind.Defeat) ShowOutcome(BattleOutcome.Defeat, e.Battle);
                        else if (e.Outcome == CombatEndKind.Left) ShowOutcome(BattleOutcome.Fled, e.Battle);
                    }
                    break;
                case SessionEventKind.RaidStarted:
                    wipeBanner = null;
                    break;
                case SessionEventKind.SpecialOutcome:
                    if (e.Amount == 1 && !string.IsNullOrEmpty(e.Text))
                        AddBanner(new Banner { Kind = BannerKind.Story, Title = e.Text, Color = Ui.Gold, Life = 5.5f });
                    break;
                case SessionEventKind.TimeOfDayChanged:
                    AddTimeOfDay(e.Id);
                    break;
            }
        }

        /// <summary>True right after a level-up of the unit (its learned ranks / talent points are on the level-up card).</summary>
        bool FromLevelUp(Unit u) => u != null && levelUpAt.TryGetValue(u, out var t) && Time.unscaledTime - t < 2f;

        void OnCombatEvent(CombatEvent e)
        {
            try
            {
                if (e == null) return;
                if (e.Type == CombatEventType.BattleEnd)
                {
                    var bb = Hud.Battle;
                    if (bb != null && bb.IsOver && bannerBattle != bb)
                    {
                        bannerBattle = bb;
                        ShowOutcome(bb.Outcome, bb);
                    }
                    return;
                }
                if (e.Type != CombatEventType.CastStart || e.Source == null) return;
                if (e.Reason == "continuing") return;
                var u = e.Source;
                var p = u.Pending;
                if (p == null || p.Ability == null) return;   // resolved immediately: not a telegraph
                var b = Hud.Battle;
                bool party = b != null ? u.Team == b.PlayerTeam : u.Team == Team.Player;
                string name = Hud.NameOf(u);
                if (party)
                    Add(Kind.Info, name + " begins casting " + p.Ability.name + " — it resolves at the start of their next turn.", "glyph_hourglass", Hud.SchoolCol(p.Ability.school), "cast:" + u.Id + ":" + p.Ability.id);
                else
                    Add(Kind.Warn, name + " begins casting " + p.Ability.name + "! Interrupt it before its next turn.", "glyph_hourglass", WarnCol, "ecast:" + u.Id + ":" + p.Ability.id);
            }
            catch (Exception ex) { Hud.LogOnce("toast-cast", "Cast toast: " + ex.Message); }
        }

        // ================================================================ builders

        Toast Add(Kind kind, string text, string glyph, Color accent, string key, string plain = null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            // same key still on screen: refresh it instead of stacking a duplicate
            var existing = Find(key);
            if (existing != null)
            {
                existing.Age = Mathf.Min(existing.Age, FadeIn);
                existing.Count++;
                return existing;
            }
            var t = new Toast
            {
                Kind = kind, Text = text, Plain = plain ?? HudText.Strip(text), Glyph = glyph, Accent = accent, Key = key ?? "",
                Life = Mathf.Clamp(3.2f + text.Length * 0.03f, 3.2f, 7f), Count = 1,
            };
            if (active.Count < MaxVisible) active.Add(t); else waiting.Add(t);
            if (waiting.Count > 12) waiting.RemoveAt(0);
            return t;
        }

        Toast Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < active.Count; i++) if (active[i].Key == key && active[i].Age < active[i].Life - FadeOut) return active[i];
            for (int i = 0; i < waiting.Count; i++) if (waiting[i].Key == key) return waiting[i];
            return null;
        }

        void AddItem(SessionEvent e, bool received)
        {
            var item = e.Item;
            string id = !string.IsNullOrEmpty(e.Id) ? e.Id : item != null ? item.Id : "";
            string name = item != null ? item.Name : (Hud.Db != null && Hud.Db.Item(id) != null ? Hud.Db.Item(id).name : id);
            var q = item != null && item.Def != null ? item.Def.quality : (Hud.Db != null && Hud.Db.Item(id) != null ? Hud.Db.Item(id).quality : Quality.Common);
            string glyph = item != null && item.Def != null && !string.IsNullOrEmpty(item.Def.icon) ? item.Def.icon : "glyph_coin";
            string key = (received ? "item+:" : "item-:") + id;
            int n = Mathf.Max(1, e.Amount);
            var t = Find(key);
            if (t != null) { t.Amount += n; t.Age = Mathf.Min(t.Age, FadeIn); }
            else
            {
                t = Add(received ? Kind.Item : Kind.Info, "x", glyph, Ui.QualityColor(q), key);
                if (t == null) return;
                t.Amount = n;
            }
            string prefix = received ? "Received: " : "Lost: ";
            string count = t.Amount > 1 ? "  ×" + t.Amount.ToString(CultureInfo.InvariantCulture) : "";
            t.Text = prefix + Ui.Rich("[" + name + "]", Ui.QualityColor(q)) + count;
            t.Plain = prefix + "[" + name + "]" + count;
            t.Width = -1f;
        }

        void AddMoney(int delta)
        {
            if (delta == 0) return;
            var t = Find("money");
            if (t != null) { t.Amount += delta; t.Age = Mathf.Min(t.Age, FadeIn); }
            else
            {
                t = Add(Kind.Money, "x", "glyph_coin", MoneyCol, "money");
                if (t == null) return;
                t.Amount = delta;
            }
            int a = t.Amount;
            t.Text = (a >= 0 ? "+" : "-") + Ui.Money(Math.Abs(a));
            t.Plain = HudText.Strip(t.Text);
            t.Width = -1f;
        }

        void AddXp(int amount)
        {
            if (amount <= 0) return;
            var t = Find("xp");
            if (t != null) { t.Amount += amount; t.Age = Mathf.Min(t.Age, FadeIn); }
            else
            {
                t = Add(Kind.Xp, "x", "glyph_sparkle", Hud.Xp, "xp");
                if (t == null) return;
                t.Amount = amount;
                t.Scale = 0.9f;
            }
            t.Text = "+" + t.Amount.ToString(CultureInfo.InvariantCulture) + " experience";
            t.Plain = t.Text;
            t.Width = -1f;
        }

        void AddLearned(SessionEvent e)
        {
            var a = Hud.Db != null ? Hud.Db.Ability(e.Id) : null;
            string who = Hud.NameOf(e.Unit);
            string key = "learn:" + who;
            var t = Find(key);
            if (t != null)
            {
                t.Amount++;
                t.Age = Mathf.Min(t.Age, FadeIn);
                t.Text = who + " learned " + t.Amount + " new abilities and ranks.";
                t.Plain = t.Text;
                t.Width = -1f;
                return;
            }
            string txt = !string.IsNullOrEmpty(e.Text) ? e.Text : who + " learned " + (a != null ? a.name : e.Id) + ".";
            t = Add(Kind.Ability, txt, a != null && !string.IsNullOrEmpty(a.icon) ? a.icon : "glyph_sparkle", a != null ? Hud.SchoolCol(a.school) : Ui.Gold, key);
            if (t != null) t.Amount = 1;
        }

        void AddCheck(SessionEvent e)
        {
            // conversation checks are rolled on screen by the dialogue window (tumbling d20, then the verdict): a toast
            // would give the result away before the die lands. Only world checks (locks, Pick Lock) are toasted.
            var s = Hud.Session;
            if (s != null && (s.Mode == SessionMode.Dialogue || (s.Dialogue != null && s.Dialogue.IsActive))) return;
            var c = e.Check;
            if (c == null)
            {
                if (!string.IsNullOrEmpty(e.Text)) Add(Kind.Check, e.Text, "glyph_star", InfoCol, "chk:" + e.Text);
                return;
            }
            string skill = UiText.Spaced(c.Skill.ToString());
            string outcome = c.Critical ? "Natural 20 — Success!" : c.Fumble ? "Natural 1 — Failure" : (c.Success ? "Success" : "Failure");
            string mod = c.Modifier >= 0 ? " + " + c.Modifier : " - " + (-c.Modifier);
            string who = string.IsNullOrEmpty(c.RollerName) ? "" : c.RollerName + " · ";
            string body = who + skill + " check: " + c.Roll + mod + " = " + c.Total + " vs DC " + c.Dc + " — ";
            string text = body + Ui.Rich(outcome, c.Success ? CheckGood : CheckBad);
            var t = Add(Kind.Check, text, c.Success ? "glyph_star" : "glyph_skull", c.Success ? CheckGood : CheckBad, "chk:" + body + outcome, body + outcome);
            if (t != null) t.Life += 1.2f;
        }

        void AddLevelUp(SessionEvent e)
        {
            int level = e.Amount > 0 ? e.Amount : (e.Unit != null ? e.Unit.Level : 0);
            string who = Hud.NameOf(e.Unit);
            // merge the party's simultaneous level-ups into one banner
            for (int i = 0; i < banners.Count; i++)
            {
                var b = banners[i];
                if (b.Kind != BannerKind.LevelUp || (i == 0 && b.Age > 1.2f)) continue;
                if (level > b.Level) { b.Level = level; b.Title = "Level " + level; }
                if (!b.Names.Contains(who)) b.Names.Add(who);
                b.Sub = string.Join("  ·  ", b.Names);
                return;
            }
            // short on purpose: "Level 12" and who — the panels' level-up card (bottom right) has the details
            var nb = new Banner { Kind = BannerKind.LevelUp, Title = "Level " + level, Level = level, Color = Ui.Gold, Life = 3f };
            nb.Names.Add(who);
            nb.Sub = who;
            AddBanner(nb);
        }

        void AddTimeOfDay(string phase)
        {
            string text, glyph;
            switch (phase)
            {
                case "dawn": text = "Dawn breaks over the valley."; glyph = "glyph_sun"; break;
                case "day": text = "The sun climbs high."; glyph = "glyph_sun"; break;
                case "dusk": text = "Dusk falls; the lanterns stir."; glyph = "glyph_sun"; break;
                case "night": text = "Night settles in."; glyph = "glyph_moon"; break;
                default: return;
            }
            var t = Add(Kind.Time, text, glyph, Hud.C("#c9d6f0"), "tod:" + phase);
            if (t != null) t.Scale = 0.9f;
        }

        void AddError(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < errors.Count; i++)
                if (errors[i].Text == text) { errors[i].Age = 0f; errors[i].Count++; return; }
            errors.Add(new ErrorLine { Text = text });
            while (errors.Count > 3) errors.RemoveAt(0);
            Ui.Sfx?.Invoke("ui_close");
        }

        void AddBanner(Banner b)
        {
            if (banners.Count >= 6) banners.RemoveAt(banners.Count - 1);
            banners.Add(b);
        }

        void ClearAll()
        {
            wipeBanner = null;
            active.Clear();
            waiting.Clear();
            banners.Clear();
            errors.Clear();
            levelUpAt.Clear();
        }

        static string QuestName(SessionEvent e)
        {
            if (e.Quest != null && !string.IsNullOrEmpty(e.Quest.QuestName)) return e.Quest.QuestName;
            var db = Hud.Db;
            if (db != null && e.Id != null && db.Quests.TryGetValue(e.Id, out var q) && q != null && !string.IsNullOrEmpty(q.name)) return q.name;
            return e.Id ?? "";
        }

        /// <summary>"Old Kusu — the great camphor tree…" → title + subtitle; "The Old Lantern Shrine. Above you…" likewise.</summary>
        static void SplitRegion(string text, out string title, out string sub)
        {
            text = text.Trim();
            int i = text.IndexOf(" — ", StringComparison.Ordinal);
            if (i < 0) i = text.IndexOf(" - ", StringComparison.Ordinal);
            if (i > 0 && i < 60) { title = text.Substring(0, i).Trim(); sub = text.Substring(i + 3).Trim(); return; }
            int d = text.IndexOf(". ", StringComparison.Ordinal);
            if (d > 0 && d < 50) { title = text.Substring(0, d).Trim(); sub = text.Substring(d + 2).Trim(); return; }
            if (text.Length <= 40) { title = text.TrimEnd('.'); sub = ""; return; }
            title = "";
            sub = text;
        }

        // ================================================================ draw

        public void Draw()
        {
            if (!HudDraw.IsRepaint) return;   // visual only
            try
            {
                HudStyles.Ensure();
                bool combat = Hud.Battle != null;
                DrawBanner(combat);
                DrawErrors();   // the toasts are drawn by ToastLaneHud (Order 480: above windows and menus)
            }
            catch (Exception e) { Hud.LogOnce("toast-draw:" + e.GetType().Name, "Toasts draw: " + e); }
        }

        static readonly string[] times = new string[100];
        static string Times(int n)
        {
            n = Mathf.Clamp(n, 0, 99);
            return times[n] ??= "×" + n.ToString(CultureInfo.InvariantCulture);
        }

        static float Fade(float age, float life, float fin = FadeIn, float fout = FadeOut)
        {
            if (age < fin) return Mathf.Clamp01(age / fin);
            if (age > life - fout) return Mathf.Clamp01((life - age) / fout);
            return 1f;
        }

        /// <summary>Top of the toast lane: below the target frame in combat, under the clock row in exploration, at the
        /// very top in conversations, menus and windows over the HUD.</summary>
        static float LaneTop()
        {
            if (Hud.InDialogue || !Hud.WorldHud) return 40f;
            // combat: below the target frame (+ its cast bar and two rows of auras)
            return Hud.CombatHud ? TurnOrderHud.Bottom + 8f + 96f + 26f + 66f + 12f : 78f;
        }

        /// <summary>Lowest y the lane may use: above the error lane (room for its three lines) while the HUD is up, above
        /// the dialogue window in conversations, the upper half of the screen in menus.</summary>
        static float LaneLimit()
        {
            if (Hud.InDialogue) return Ui.Height * 0.5f;
            if (Hud.WorldHud) return HudLayout.ErrorY - 26f * 2f - 10f;
            return Ui.Height * 0.45f;
        }

        /// <summary>Draws the toast lane (called by ToastLaneHud on Repaint).</summary>
        internal void DrawLane()
        {
            HudStyles.Ensure();
            DrawToasts();
        }

        void DrawToasts()
        {
            float top = LaneTop(), limit = LaneLimit();
            float y = top;
            int fit = 0;
            laneBottom = top;
            if (active.Count == 0) { laneFit = Mathf.Max(1, Mathf.FloorToInt((limit - top) / (ToastH + 6f))); return; }
            var st = HudStyles.ToastText;
            for (int i = 0; i < active.Count; i++)
            {
                var t = active[i];
                // never down into the error lane / dialogue window: the rest waits (it is shown as the older ones fade)
                if (y + ToastH * t.Scale > limit && fit > 0) break;
                fit++;
                float a = Fade(t.Age, t.Life);
                if (t.Width < 0f)
                {
                    measure.text = t.Plain;
                    t.Width = Mathf.Min(st.CalcSize(measure).x, Ui.Width - 140f);
                }
                float h = ToastH * t.Scale;
                float w = t.Width + (t.Glyph != null ? 44f : 28f);
                float slide = (1f - Mathf.Clamp01(t.Age / FadeIn)) * -10f;
                var r = new Rect(Mathf.Round(Ui.Width * 0.5f - w * 0.5f), Mathf.Round(y + slide), w, h);
                var back = new Color(0.09f, 0.07f, 0.15f, 0.8f * a);
                HudDraw.Fill(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), new Color(0f, 0f, 0f, 0.18f * a), 8);
                HudDraw.Fill(r, back, 8);
                var acc = t.Accent;
                HudDraw.Ring(r, new Color(acc.r, acc.g, acc.b, 0.55f * a), 8);
                if (t.Kind == Kind.QuestDone) HudDraw.Glow(new Rect(r.x - 30f, r.y - 18f, r.width + 60f, r.height + 36f), new Color(1f, 0.85f, 0.4f, 0.28f * a));
                float tx = r.x + 14f;
                if (t.Glyph != null)
                {
                    HudDraw.Glyph(new Rect(r.x + 10f, r.y + (h - 20f) * 0.5f, 20f, 20f), t.Glyph, new Color(acc.r, acc.g, acc.b, a));
                    tx = r.x + 36f;
                }
                var tr = new Rect(tx, r.y, r.xMax - tx - 8f, h);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                Color textCol = t.Kind == Kind.Quest || t.Kind == Kind.QuestDone ? Ui.TextLight
                              : t.Kind == Kind.Warn ? WarnCol : t.Kind == Kind.Xp ? Hud.Xp : t.Kind == Kind.Notice ? acc : Ui.TextLight;
                if (t.Text.IndexOf('<') >= 0) HudDraw.Rich(tr, t.Text, st, t.Plain);
                else HudDraw.Text(tr, t.Text, st, textCol);
                GUI.color = old;
                if (t.Count > 1 && t.Kind != Kind.Item && t.Kind != Kind.Money && t.Kind != Kind.Xp && t.Kind != Kind.Ability)
                    HudDraw.Text(new Rect(r.xMax - 40f, r.y - 8f, 44f, 16f), Times(t.Count), HudStyles.TinyRight, new Color(1f, 1f, 1f, 0.7f * a));
                y += h + 6f;
            }
            laneBottom = y;
            // how many fit at the standard height (Age only promotes waiting toasts into the room there is)
            laneFit = Mathf.Max(1, Mathf.FloorToInt((limit - top) / (ToastH + 6f)));
        }

        void DrawErrors()
        {
            if (errors.Count == 0) return;
            float y = HudLayout.ErrorY;
            var st = HudStyles.Center;
            for (int i = errors.Count - 1; i >= 0; i--)
            {
                var e = errors[i];
                float a = Fade(e.Age, 2.9f, 0.08f, 0.7f);
                var r = new Rect(Ui.Width * 0.5f - 420f, y, 840f, 26f);
                HudDraw.Text(r, e.Text, st, new Color(1f, 0.42f, 0.36f, a));
                y -= 26f;
            }
        }

        static float BannerHalfHeight(BannerKind k)
        {
            switch (k)
            {
                case BannerKind.MapTitle: return 48f;
                case BannerKind.Region: return 34f;
                case BannerKind.LevelUp: return 50f;
                case BannerKind.Story: return 56f;
                case BannerKind.Combat:
                case BannerKind.Left: return 36f;
                default: return 52f;
            }
        }

        void DrawBanner(bool combat)
        {
            if (banners.Count == 0) return;
            var b = banners[0];
            if (Hud.InDialogue && (b.Kind == BannerKind.Region || b.Kind == BannerKind.MapTitle)) return;   // wait for the conversation
            float a = Fade(b.Age, b.Life, 0.5f, 0.9f);
            float cy = combat ? Ui.Height * 0.27f : Ui.Height * 0.22f;
            if (Hud.InDialogue) cy = Ui.Height * 0.14f;
            // never under the toast lane: start below the toasts on screen (laneBottom from the lane's last draw)
            if (active.Count > 0) cy = Mathf.Max(cy, laneBottom + BannerHalfHeight(b.Kind) + 12f);
            float cx = Ui.Width * 0.5f;
            float rise = (1f - Mathf.Clamp01(b.Age / 0.5f)) * 14f;
            cy += rise;

            switch (b.Kind)
            {
                case BannerKind.MapTitle:
                case BannerKind.Region:
                {
                    bool big = b.Kind == BannerKind.MapTitle;
                    var ts = big ? HudStyles.TitleHuge : HudStyles.TitleBig;
                    float th = big ? 80f : 52f;
                    if (!string.IsNullOrEmpty(b.Title))
                    {
                        var tr = new Rect(cx - 700f, cy - th * 0.5f, 1400f, th);
                        measure.text = b.Title;
                        float tw = Mathf.Min(ts.CalcSize(measure).x, 1300f);
                        // ornamental gold rules either side of the title
                        float lw = big ? 160f : 110f;
                        var gc = new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.75f * a);
                        HudDraw.Solid(new Rect(cx - tw * 0.5f - 24f - lw, cy + 2f, lw, 2f), gc);
                        HudDraw.Solid(new Rect(cx + tw * 0.5f + 24f, cy + 2f, lw, 2f), gc);
                        HudDraw.Glyph(new Rect(cx - tw * 0.5f - 22f, cy - 7f, 16f, 16f), "glyph_sparkle", gc);
                        HudDraw.Glyph(new Rect(cx + tw * 0.5f + 6f, cy - 7f, 16f, 16f), "glyph_sparkle", gc);
                        HudDraw.Glow(new Rect(cx - tw * 0.6f, cy - th, tw * 1.2f, th * 2f), new Color(0.1f, 0.06f, 0.2f, 0.35f * a));
                        HudDraw.Text(tr, b.Title, ts, new Color(b.Color.r, b.Color.g, b.Color.b, a));
                    }
                    if (!string.IsNullOrEmpty(b.Sub))
                    {
                        var sr = new Rect(cx - 380f, cy + th * 0.5f + 2f, 760f, 60f);
                        HudDraw.Text(sr, b.Sub, HudStyles.Subtitle, new Color(1f, 0.93f, 0.8f, 0.92f * a));
                    }
                    break;
                }
                case BannerKind.LevelUp:
                {
                    HudDraw.Glow(new Rect(cx - 300f, cy - 120f, 600f, 240f), new Color(1f, 0.8f, 0.35f, 0.35f * a));
                    HudDraw.Text(new Rect(cx - 500f, cy - 44f, 1000f, 80f), b.Title, HudStyles.TitleHuge, new Color(1f, 0.86f, 0.45f, a));
                    HudDraw.Text(new Rect(cx - 500f, cy + 34f, 1000f, 30f), b.Sub, HudStyles.Subtitle, new Color(1f, 0.96f, 0.86f, a));
                    break;
                }
                case BannerKind.Victory:
                case BannerKind.Defeat:
                case BannerKind.Left:
                case BannerKind.Combat:
                {
                    bool small = b.Kind == BannerKind.Combat || b.Kind == BannerKind.Left;
                    var ts = small ? HudStyles.TitleBig : HudStyles.TitleHuge;
                    float th = small ? 54f : 84f;
                    var col = b.Color;
                    HudDraw.Glow(new Rect(cx - 320f, cy - th * 1.3f, 640f, th * 2.6f), new Color(col.r, col.g, col.b, (b.Kind == BannerKind.Defeat ? 0.18f : 0.3f) * a));
                    // ribbon
                    var rib = new Rect(cx - 300f, cy - th * 0.42f, 600f, th * 0.84f);
                    HudDraw.Fill(rib, new Color(0.1f, 0.07f, 0.17f, 0.55f * a), 12);
                    HudDraw.Solid(new Rect(rib.x + 30f, rib.y, rib.width - 60f, 2f), new Color(col.r, col.g, col.b, 0.7f * a));
                    HudDraw.Solid(new Rect(rib.x + 30f, rib.yMax - 2f, rib.width - 60f, 2f), new Color(col.r, col.g, col.b, 0.7f * a));
                    HudDraw.Text(new Rect(cx - 500f, cy - th * 0.5f, 1000f, th), b.Title, ts, new Color(col.r, col.g, col.b, a));
                    if (!string.IsNullOrEmpty(b.Sub))
                        HudDraw.Text(new Rect(cx - 420f, rib.yMax + 6f, 840f, 30f), b.Sub, HudStyles.Subtitle, new Color(1f, 0.95f, 0.88f, 0.92f * a));
                    break;
                }
                case BannerKind.Story:
                {
                    HudDraw.Glow(new Rect(cx - 420f, cy - 110f, 840f, 220f), new Color(1f, 0.85f, 0.45f, 0.3f * a));
                    var r = new Rect(cx - 460f, cy - 50f, 920f, 100f);
                    var st = HudStyles.TitleBig;
                    st.wordWrap = true;
                    HudDraw.Text(r, b.Title, st, new Color(1f, 0.9f, 0.6f, a));
                    st.wordWrap = false;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The single toast lane, drawn above windows, menus and the HUD (Order 480; visual only — no input) so session toasts,
    /// flow toasts and the panels' notices (GameFlow.Toast) are seen wherever the player is: the toasts themselves live in
    /// ToastsHud (intake, merging, ageing); this screen only draws them.
    /// </summary>
    public sealed class ToastLaneHud : IUiScreen
    {
        public const int LaneOrder = 480;
        public string Id => "";
        public int Order => LaneOrder;
        public bool Visible { get { var t = ToastsHud.Current; return t != null && t.HasToasts; } }
        public bool Modal => false;
        public void Tick(float dt) { }

        public void Draw()
        {
            if (!HudDraw.IsRepaint) return;
            try { ToastsHud.Current?.DrawLane(); }
            catch (Exception e) { Hud.LogOnce("toast-lane:" + e.GetType().Name, "Toast lane: " + e); }
        }
    }
}
