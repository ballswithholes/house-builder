// Party & camp roster (UiPanels.Party; K, or the Journal's "Party & camp" button): every recruited character with portrait,
// class and level, status (leader / in the party / at camp / away), approval; make leader, send to camp / join the
// party (out of combat), companion auto-play (the AI plays their turns), dismiss with confirmation.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class PartyPanel : PanelWindow
    {
        public override string Id => UiPanels.Party;
        // above the journal (130) and the map (132), which it is opened from / centred over; below the talents (140)
        public override int Order => 134;

        public override void Tick(float dt)
        {
            // K toggles the roster (UiRoot's hotkey table has no entry for it); same gates as UiRoot.HandleHotkeys
            if (!GameInput.KeyDown(KeyCode.K) || UiRoot.HotkeysSuppressed || !GameFlow.HasGame || UiRoot.ModalActive) return;
            UiRoot.Toggle(Id);
        }

        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        readonly List<Unit> roster = new List<Unit>();
        string headText = "";
        int headStamp = -1;

        sealed class RowText
        {
            public int Stamp = -1;
            public string Level = "", Approval = "";
        }

        readonly Dictionary<Unit, RowText> rowTexts = new Dictionary<Unit, RowText>();

        RowText TextOf(Unit u, GameSession s)
        {
            int a = u.Companion != null ? s.GetApproval(u.Companion.id) : 0;
            int stamp = u.Level * 1000 + a;
            if (!rowTexts.TryGetValue(u, out var t)) { t = new RowText(); rowTexts[u] = t; }
            if (t.Stamp == stamp) return t;
            t.Stamp = stamp;
            t.Level = $"Level {u.Level} {(u.Class != null ? u.Class.name : "")}";
            t.Approval = u.Companion != null ? $"Approval {a:+0;-0;0} · {CharacterPanel.ApprovalWord(a)}" : "";
            return t;
        }

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            roster.Clear();
            roster.AddRange(s.Roster);
            float h = Mathf.Min(Ui.Height - 140f, 170f + roster.Count * 104f + 40f);
            var r = PanelKit.Centered(Mathf.Min(980f, Ui.Width - 40f), Mathf.Max(420f, h), -20f);
            var c = Chrome(r, "Party & camp");
            int stamp = s.Party.Count * 100 + s.PartySize;
            if (stamp != headStamp)
            {
                headStamp = stamp;
                headText = $"Travelling together: <b>{s.Party.Count} / {s.PartySize}</b>. Companions at camp wait for you and keep their gear and levels.";
            }
            PanelKit.Label(new Rect(c.x, c.y, c.width, 50f), headText, PanelKit.TextSmall);
            bool explore = s.Mode == SessionMode.Exploration;
            var list = new Rect(c.x, c.y + 56f, c.width, c.height - 64f);
            const float rowH = 104f;
            float cw = PanelKit.BeginScroll(list, scroll, roster.Count * rowH);
            try
            {
                for (int i = 0; i < roster.Count; i++)
                {
                    var u = roster[i];
                    var row = new Rect(0f, i * rowH, cw, rowH - 8f);
                    if (u == null || !scroll.IsVisible(row)) continue;
                    DrawRow(row, u, s, explore);
                }
            }
            finally { PanelKit.EndScroll(scroll); }
        }

        void DrawRow(Rect row, Unit u, GameSession s, bool explore)
        {
            string id = s.MemberId(u);
            bool isMain = u == s.Main;
            var status = isMain ? CompanionStatus.Active : s.CompanionStatusOf(id);
            bool inParty = s.IsInParty(u);
            bool leader = s.Leader == u;
            bool away = status == CompanionStatus.Away;
            PanelKit.Rounded(row, leader ? PanelKit.RowSelected : PanelKit.Hover(row) ? PanelKit.RowHover : PanelKit.RowShade);
            Ui.Portrait(new Rect(row.x + 10f, row.y + 10f, 76f, 76f), PanelKit.PortraitOf(u), PanelKit.ColorOf(u), !inParty);
            if (leader)
            {
                var crown = Ui.GlyphTexture("crown");
                if (crown != null) PanelKit.Tex(new Rect(row.x + 64f, row.y + 2f, 28f, 28f), crown, Ui.GoldDeep, ScaleMode.ScaleToFit);
            }
            float x = row.x + 100f;
            PanelKit.Label(new Rect(x, row.y + 8f, 300f, 30f), PanelKit.NameOf(u), PanelKit.RowText, u.Class != null ? PanelKit.InkColorOf(u.Class.id) : Ui.Ink);
            var tx = TextOf(u, s);
            PanelKit.Label(new Rect(x, row.y + 36f, 300f, 24f), tx.Level, PanelKit.RowTextSmall);
            string st = leader ? "Leading the party" : inParty ? "In the party" : status == CompanionStatus.Camp ? "Resting at camp" : away ? "Went their own way — talk to them to ask again" : "";
            PanelKit.Label(new Rect(x, row.y + 60f, away ? 600f : 220f, 24f), st, PanelKit.RowTextSmall, leader ? PanelKit.GoldInk : Ui.InkSoft);
            if (u.Companion != null && !away)
            {
                int a = s.GetApproval(u.Companion.id);
                PanelKit.Label(new Rect(x + 226f, row.y + 60f, 190f, 24f), tx.Approval, PanelKit.RowTextSmall, a >= 10 ? PanelKit.GoodDark : a <= -10 ? PanelKit.BadDark : Ui.InkSoft);
            }
            if (away) return;

            // buttons (right side, two rows)
            float bx = row.xMax - 404f, by = row.y + 8f;
            if (inParty && !leader)
            {
                if (PanelKit.Btn(new Rect(bx, by, 128f, 38f), "Lead", PanelKit.SmallButton, explore, explore ? "Make them the leader (the one you move)." : "Only while exploring."))
                {
                    var who = u;
                    PanelKit.Do(() => PanelKit.Flow?.Select(who));
                }
            }
            if (!isMain)
            {
                if (inParty)
                {
                    if (PanelKit.Btn(new Rect(bx + 136f, by, 128f, 38f), "To camp", PanelKit.SmallButton, explore, explore ? "They wait at camp until you call them back." : "Only while exploring."))
                    {
                        string cid = id;
                        PanelKit.Do(() => { var ss = PanelKit.Sess; if (ss != null) PanelKit.Try(() => ss.SetPartyMemberActive(cid, false)); });
                    }
                }
                else
                {
                    bool room = s.Party.Count < s.PartySize;
                    if (PanelKit.Btn(new Rect(bx + 136f, by, 128f, 38f), "Join party", PanelKit.SmallButtonGold, explore && room, !room ? "The party is full. Send someone to camp first." : explore ? null : "Only while exploring."))
                    {
                        string cid = id;
                        PanelKit.Do(() => { var ss = PanelKit.Sess; if (ss != null) PanelKit.Try(() => ss.SetPartyMemberActive(cid, true)); });
                    }
                }
                if (PanelKit.Btn(new Rect(bx + 272f, by, 128f, 38f), "Dismiss", PanelKit.SmallButtonDark, explore, explore ? "They leave the party and go back to where you met them." : "Only while exploring."))
                {
                    string cid = id;
                    string name = PanelKit.NameOf(u);
                    ConfirmScreen.Ask("Dismiss " + name + "?", $"{name} leaves and returns to where you met. You can talk to them to ask them back.", "Dismiss", () =>
                    {
                        var ss = PanelKit.Sess;
                        if (ss == null) return;
                        try { ss.Dismiss(cid); } catch (Exception e) { Debug.LogException(e); PanelKit.Notice("Could not dismiss them."); }
                    }, "Keep", null, true);
                }
            }
            if (inParty)
            {
                bool auto = u.AutoPlay;
                bool nv = SettingsView.Toggle(new Rect(bx, by + 46f, 400f, 34f), isMain ? "Auto-play (the AI fights for you)" : "Auto-play in combat", auto,
                    "The companion AI plays this character's turns (and its pet's).", false);
                if (nv != auto)
                {
                    var who = u;
                    bool on = nv;
                    PanelKit.Do(() =>
                    {
                        var f = PanelKit.Flow;
                        if (f != null && f.Combat != null) f.Combat.SetAutoPlay(who, on);
                        else PanelKit.Sess?.SetAutoPlay(who, on);
                    });
                }
            }
        }
    }
}
