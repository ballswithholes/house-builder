// Trainer window (Session.ActiveTrainer, Order 143: above the sheets, like the vendor, so an open Spellbook or Talents
// window never hides it): tabs for every party member the trainer can teach; per ability the highest rank trainable now
// (lower missing ranks are paid too) with its total cost, "New" badges, the next ranks that unlock later (level
// requirement), tooltips with numbers; Train / Train All. Members of other classes get a hint.
// Respec window (Session.ActiveRespecNpc, Order 200): unlearn a member's talents for the escalating fee, then the
// Talents window opens for that member.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class TrainerScreen : IUiScreen
    {
        public const int ScreenOrder = 143;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Modal => false;

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && s.ActiveTrainer != null && s.Mode == SessionMode.Exploration;
            }
        }

        static TrainerScreen instance;

        sealed class Row
        {
            public AbilityDef Ability;
            public int Rank, FromRank, Level, Cost;
            public bool Now, Afford, IsNew;
            public string RankText = "", LevelText = "", Sub = "", Tip;
        }

        readonly List<Row> rows = new List<Row>();
        readonly List<Unit> members = new List<Unit>();
        readonly List<Unit> others = new List<Unit>();
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        Unit selected;
        NpcDef lastTrainer;
        float refreshAt;
        int totalNow, countNow, countAfford;
        string trainsText = "", memberText = "", affordTip = "";

        public TrainerScreen()
        {
            instance = this;
            PanelKit.Events += e =>
            {
                if (e.Kind == SessionEventKind.TrainerOpened || e.Kind == SessionEventKind.AbilityLearned || e.Kind == SessionEventKind.GoldChanged || e.Kind == SessionEventKind.LevelUp)
                    refreshAt = 0f;
            };
        }

        static TrainerScreen()
        {
            EscRouter.Register(300, () =>
            {
                var t = instance;
                if (t == null || !t.Visible) return false;
                PanelKit.Do(() => PanelKit.Sess?.CloseTrainer());
                return true;
            }, ScreenOrder);
        }

        public void Tick(float dt)
        {
            if (!Visible) { lastTrainer = null; return; }
            var s = PanelKit.Sess;
            if (s.ActiveTrainer != lastTrainer)
            {
                lastTrainer = s.ActiveTrainer;
                selected = null;
                scroll.Reset();
                refreshAt = 0f;
                var names = new List<string>();
                var db = PanelKit.Db;
                if (lastTrainer.trains != null)
                    foreach (var c in lastTrainer.trains) { var d = db != null ? db.Class(c) : null; names.Add(d != null ? d.name : c.ToString()); }
                trainsText = names.Count > 0 ? "Teaches " + string.Join(", ", names) : "";
            }
            if (Time.unscaledTime >= refreshAt) Refresh(s);
        }

        void Refresh(GameSession s)
        {
            refreshAt = Time.unscaledTime + 0.5f;
            members.Clear();
            others.Clear();
            foreach (var u in s.Party)
            {
                if (u == null || u.Class == null) continue;
                if (s.CanTrainHere(u)) members.Add(u); else others.Add(u);
            }
            if (selected == null || !members.Contains(selected))
            {
                var m = PanelKit.Member;
                selected = m != null && members.Contains(m) ? m : (members.Count > 0 ? members[0] : null);
            }
            rows.Clear();
            totalNow = countNow = countAfford = 0;
            memberText = selected != null ? $"<b>{PanelKit.NameOf(selected)}</b>\nLevel {selected.Level} {selected.Class.name}" : "";
            if (selected == null) return;
            List<TrainerOffer> offers;
            try { offers = s.TrainerOffers(selected); }
            catch (Exception e) { Debug.LogException(e); return; }
            var byAbility = new Dictionary<AbilityDef, Row>();
            var later = new List<Row>();
            int gold = s.Gold;
            foreach (var o in offers)
            {
                if (o == null || o.Ability == null) continue;
                bool levelOk = o.Level <= selected.Level;
                if (levelOk)
                {
                    if (!byAbility.TryGetValue(o.Ability, out var row))
                    {
                        row = new Row { Ability = o.Ability, FromRank = o.Rank, Now = true, IsNew = selected.RankOf(o.Ability.id) == 0 };
                        byAbility[o.Ability] = row;
                        rows.Add(row);
                    }
                    if (o.Rank > row.Rank) { row.Rank = o.Rank; row.Level = o.Level; }
                    row.Cost += o.Cost;
                }
                else
                {
                    later.Add(new Row { Ability = o.Ability, Rank = o.Rank, FromRank = o.Rank, Level = o.Level, Cost = o.Cost, Now = false, IsNew = selected.RankOf(o.Ability.id) == 0 });
                }
            }
            int running = gold;
            rows.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            foreach (var r in rows)
            {
                r.Afford = r.Cost <= gold;
                totalNow += r.Cost;
                countNow++;
                if (r.Cost <= running) { running -= r.Cost; countAfford++; }
            }
            affordTip = $"You can afford {countAfford} of {countNow}.";
            rows.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Ability.name, b.Ability.name));
            later.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : string.CompareOrdinal(a.Ability.name, b.Ability.name));
            rows.AddRange(later);
            foreach (var r in rows)
            {
                int count = AbilityRules.RankCount(r.Ability);
                r.RankText = count <= 1 ? (r.IsNew ? "New ability" : "") : r.FromRank != r.Rank ? $"Ranks {r.FromRank}–{r.Rank}" : $"Rank {r.Rank}";
                if (r.IsNew && count > 1) r.RankText = "New · " + r.RankText;
                r.LevelText = r.Now ? "Level " + r.Level : Ui.Rich("Requires level " + r.Level, PanelKit.BadDark);
                r.Sub = r.RankText.Length > 0 ? r.RankText + "  ·  " + r.LevelText : r.LevelText;
            }
        }

        string TipOf(Row r)
        {
            if (r.Tip != null) return r.Tip;
            string body;
            try { body = Tooltip.Ability(selected, r.Ability, r.Rank); }
            catch (Exception) { body = r.Ability.description; }
            var head = "<b>" + r.Ability.name + "</b>" + (AbilityRules.RankCount(r.Ability) > 1 ? "  " + Ui.Rich("Rank " + r.Rank, Ui.TextMuted) : "");
            string school = r.Ability.school != School.Physical ? Ui.Rich(r.Ability.school + " · ", Ui.SchoolColor(r.Ability.school)) : "";
            r.Tip = head + "\n" + school + Ui.Rich(r.Ability.passive ? "Passive" : "Learned at level " + r.Level, Ui.TextMuted) + "\n" + body;
            return r.Tip;
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var s = PanelKit.Sess;
            var npc = s.ActiveTrainer;
            if (npc == null) return;
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                var r = PanelKit.Fit(new Rect(40f, 70f, 680f, Mathf.Min(900f, Ui.Height - 190f)));
                var c = PanelKit.Window(r, npc.name, Order, out bool close, string.IsNullOrEmpty(npc.title) ? null : npc.title);
                if (close) PanelKit.Do(() => PanelKit.Sess?.CloseTrainer());
                float y = c.y;
                PanelKit.Label(new Rect(c.x, y, c.width, 26f), trainsText, PanelKit.TextMuted);
                y += 30f;
                if (members.Count == 0)
                {
                    PanelKit.Label(new Rect(c.x, y + 20f, c.width, 90f), $"\"I have nothing to teach your companions, I'm afraid. Seek out a trainer of your own calling.\"", PanelKit.TextItalic);
                    Footer(c, s);
                    return;
                }
                var ns = PanelKit.MemberTabs(new Rect(c.x, y, c.width, 56f), selected, members, 52f);
                if (ns != selected) { selected = ns; PanelKit.Member = ns; scroll.Reset(); refreshAt = 0f; }
                if (selected != null)
                    PanelKit.Label(new Rect(c.x + members.Count * 62f + 8f, y + 4f, c.width - members.Count * 62f - 8f, 48f), memberText, PanelKit.TextSmall);
                y += 66f;
                if (others.Count > 0)
                {
                    PanelKit.Label(new Rect(c.x, y, c.width, 24f), OthersText(), PanelKit.TextMutedSmall);
                    y += 26f;
                }
                var list = new Rect(c.x, y, c.width, c.yMax - y - 120f);
                DrawRows(list, s);
                // train all
                float by = c.yMax - 108f;
                string label = countNow > 0 ? "Train all" : "Nothing to train";
                string tip = countNow == 0 ? "Come back when you have grown." : countAfford < countNow ? affordTip : null;
                if (Ui.Btn(new Rect(c.x, by, 200f, 46f), label, Ui.ButtonGold, countAfford > 0, tip)) TrainAll();
                if (countNow > 0)
                {
                    PanelKit.Label(new Rect(c.x + 214f, by + 8f, 120f, 30f), "Total", PanelKit.TextMuted);
                    PanelKit.MoneyPlate(new Rect(c.x + 260f, by + 6f, 170f, 34f), totalNow, false);
                }
                Footer(c, s);
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        string othersText = "";
        int othersStamp = -1;

        string OthersText()
        {
            int stamp = others.Count * 31 + (others.Count > 0 ? others[0].Id : 0);
            if (stamp == othersStamp) return othersText;
            othersStamp = stamp;
            var names = new List<string>();
            foreach (var u in others) names.Add(PanelKit.NameOf(u));
            othersText = string.Join(", ", names) + (others.Count == 1 ? " needs" : " need") + " a different trainer.";
            return othersText;
        }

        void Footer(Rect c, GameSession s)
        {
            PanelKit.HLine(c.x, c.yMax - 52f, c.width);
            PanelKit.Label(new Rect(c.x, c.yMax - 42f, 200f, 36f), "Your purse", PanelKit.TextMuted);
            PanelKit.MoneyPlate(new Rect(c.x, c.yMax - 42f, c.width, 36f), s.Gold);
        }

        void DrawRows(Rect list, GameSession s)
        {
            const float rowH = 66f;
            float cw = PanelKit.BeginScroll(list, scroll, rows.Count * rowH + 4f);
            try
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    var rr = new Rect(0f, i * rowH, cw, rowH - 6f);
                    if (!scroll.IsVisible(rr)) continue;
                    bool hover = PanelKit.Hover(rr);
                    PanelKit.Rounded(rr, hover ? PanelKit.RowHover : PanelKit.RowShade);
                    var a = row.Ability;
                    Ui.Icon(new Rect(rr.x + 6f, rr.y + 5f, 50f, 50f), a.icon, Ui.SchoolColor(a.school), 0f, !row.Now);
                    float nameW = rr.width - 68f - 270f;
                    PanelKit.Label(new Rect(rr.x + 68f, rr.y + 6f, nameW, 26f), a.name, PanelKit.RowText, row.Now ? Ui.Ink : Ui.InkSoft);
                    PanelKit.Label(new Rect(rr.x + 68f, rr.y + 32f, nameW + 60f, 22f), row.Sub, PanelKit.RowTextSmall, row.IsNew && row.Now ? PanelKit.GoodDark : Ui.InkSoft);
                    if (row.Cost > 0) PanelKit.MoneyPlate(new Rect(rr.xMax - 270f, rr.y + 14f, 160f, 32f), row.Cost);
                    else PanelKit.Label(new Rect(rr.xMax - 270f, rr.y + 14f, 160f, 32f), "Free", PanelKit.TextSmallRight, PanelKit.GoodDark);
                    if (row.Now)
                    {
                        string tip = row.Afford ? null : "Not enough money.";
                        if (PanelKit.Btn(new Rect(rr.xMax - 100f, rr.y + 10f, 94f, 40f), "Train", PanelKit.SmallButtonGold, row.Afford, tip)) Train(row);
                    }
                    if (hover) Ui.TooltipFor(new Rect(rr.x, rr.y, rr.width - 280f, rr.height), TipOf(row));
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (rows.Count == 0) PanelKit.Label(list, "You know everything I can teach — for now.", PanelKit.TextCenter);
        }

        void Train(Row row)
        {
            var u = selected;
            string id = row.Ability.id;
            int rank = row.Rank;
            string name = row.Ability.name;
            PanelKit.Do(() =>
            {
                var s = PanelKit.Sess;
                if (s == null || u == null) return;
                if (PanelKit.Try(() => s.Train(u, id, rank), $"{PanelKit.NameOf(u)} learned {name}" + (rank > 1 ? $" (Rank {rank})." : ".")))
                    Ui.Sfx?.Invoke("level_up");
                refreshAt = 0f;
            });
        }

        void TrainAll()
        {
            var u = selected;
            PanelKit.Do(() =>
            {
                var s = PanelKit.Sess;
                if (s == null || u == null) return;
                int n = 0;
                try { n = s.TrainAll(u); }
                catch (Exception e) { Debug.LogException(e); }
                if (n > 0) { Ui.Sfx?.Invoke("level_up"); PanelKit.Notice($"{PanelKit.NameOf(u)} learned {n} new {(n == 1 ? "ability" : "abilities")}.", false); }
                else PanelKit.Notice("Nothing could be trained.");
                refreshAt = 0f;
            });
        }
    }

    // ==================================================================================== respec

    public sealed class RespecScreen : IUiScreen
    {
        public const int ScreenOrder = 200;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Modal => false;

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && !string.IsNullOrEmpty(s.ActiveRespecNpc) && s.Mode == SessionMode.Exploration;
            }
        }

        // per-member texts and numbers, refreshed from Tick (talents learned elsewhere raise no event, so also on a timer)
        sealed class Row
        {
            public Unit Unit;
            public int Spent = -1, Cost;
            public string Name = "", SpentText = "";
        }

        readonly List<Row> rows = new List<Row>();
        string npcFor;
        string npcName = "";
        float refreshAt;

        static RespecScreen instance;

        public RespecScreen()
        {
            instance = this;
            PanelKit.Events += e =>
            {
                switch (e.Kind)
                {
                    case SessionEventKind.RespecOpened:
                    case SessionEventKind.GoldChanged:
                    case SessionEventKind.LevelUp:
                    case SessionEventKind.PartyChanged:
                    case SessionEventKind.TalentPointsAvailable:
                    case SessionEventKind.GameStarted:
                    case SessionEventKind.GameLoaded:
                        refreshAt = 0f;
                        break;
                }
            };
        }

        static RespecScreen()
        {
            EscRouter.Register(350, () =>
            {
                var r = instance;
                if (r == null || !r.Visible) return false;
                PanelKit.Do(() => PanelKit.Sess?.CloseRespec());
                return true;
            }, ScreenOrder);
        }

        public void Tick(float dt)
        {
            if (!Visible) { npcFor = null; return; }
            if (Time.unscaledTime >= refreshAt) Refresh(PanelKit.Sess);
        }

        void Refresh(GameSession s)
        {
            refreshAt = Time.unscaledTime + 0.5f;
            if (npcFor != s.ActiveRespecNpc)
            {
                npcFor = s.ActiveRespecNpc;
                npcName = s.NpcName(npcFor);
            }
            int n = 0;
            var party = s.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var u = party[i];
                if (u == null || u.Class == null) continue;
                if (n == rows.Count) rows.Add(new Row());
                var row = rows[n++];
                int spent = Progression.TalentPointsSpent(u);
                if (row.Unit != u || row.Spent != spent)
                {
                    row.Name = PanelKit.NameOf(u);
                    row.SpentText = spent <= 0 ? "No talents learned" : spent == 1 ? "1 talent point spent" : spent + " talent points spent";
                }
                row.Unit = u;
                row.Spent = spent;
                row.Cost = s.RespecCost(u);
            }
            if (rows.Count > n) rows.RemoveRange(n, rows.Count - n);
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var s = PanelKit.Sess;
            if (npcFor != s.ActiveRespecNpc) Refresh(s);   // opened after this frame's Tick
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                float h = 210f + rows.Count * 84f;
                var r = PanelKit.Centered(620f, h, -60f);
                var c = PanelKit.Window(r, "Unlearn talents", Order, out bool close, npcName);
                if (close) PanelKit.Do(() => PanelKit.Sess?.CloseRespec());
                PanelKit.Label(new Rect(c.x, c.y, c.width, 46f), "Forget every talent and spend the points anew. The fee grows each time.", PanelKit.TextSmall);
                float y = c.y + 54f;
                for (int i = 0; i < rows.Count; i++)
                {
                    var rw = rows[i];
                    var u = rw.Unit;
                    var row = new Rect(c.x, y, c.width, 76f);
                    PanelKit.Rounded(row, PanelKit.Hover(row) ? PanelKit.RowHover : PanelKit.RowShade);
                    Ui.Portrait(new Rect(row.x + 8f, row.y + 8f, 60f, 60f), PanelKit.PortraitOf(u), PanelKit.ColorOf(u));
                    int spent = rw.Spent;
                    int cost = rw.Cost;
                    PanelKit.Label(new Rect(row.x + 80f, row.y + 10f, 260f, 28f), rw.Name, PanelKit.RowText);
                    PanelKit.Label(new Rect(row.x + 80f, row.y + 38f, 260f, 24f), rw.SpentText, PanelKit.RowTextSmall);
                    if (spent > 0) PanelKit.MoneyPlate(new Rect(row.xMax - 290f, row.y + 21f, 150f, 34f), cost);
                    bool afford = s.Gold >= cost;
                    string tip = spent == 0 ? "There is nothing to unlearn." : afford ? null : "Not enough money.";
                    if (Ui.Btn(new Rect(row.xMax - 128f, row.y + 17f, 120f, 42f), "Unlearn", PanelKit.SmallButton, spent > 0 && afford, tip))
                    {
                        var who = u;
                        int fee = cost;
                        ConfirmScreen.Ask("Unlearn talents?", $"{PanelKit.NameOf(who)} forgets all {spent} talents for {Inventory.FormatMoney(fee)}.", "Unlearn", () =>
                        {
                            var ss = PanelKit.Sess;
                            if (ss == null) return;
                            if (PanelKit.Try(() => ss.Respec(who), $"{PanelKit.NameOf(who)}'s talents are reset."))
                            {
                                // the trainer's window would cover the middle talent tree: close it, then show the talents
                                ss.CloseRespec();
                                PanelKit.Member = who;
                                TalentsPanel.SelectMember(who);
                                UiRoot.Open(UiPanels.Talents);
                            }
                        }, "Keep them", null, true);
                    }
                    y += 84f;
                }
                PanelKit.Label(new Rect(c.x, r.yMax - 60f, 300f, 36f), "Your purse", PanelKit.TextMuted);
                PanelKit.MoneyPlate(new Rect(c.x + 120f, r.yMax - 60f, 200f, 36f), s.Gold, false);
                if (Ui.Btn(new Rect(c.xMax - 150f, r.yMax - 66f, 150f, 46f), "Done")) PanelKit.Do(() => PanelKit.Sess?.CloseRespec());
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }
}
