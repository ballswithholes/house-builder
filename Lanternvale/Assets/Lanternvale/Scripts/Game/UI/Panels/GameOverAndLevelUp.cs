// Game over (Session.Mode == GameOver, Order 340, modal): the battlefield fades to dusk, "The lanterns dim…", embers
// drift down; Load last save / Load… (slot list) / Main Menu.
// Level-up card (LevelUp events, Order 160; the detailed half of the level-up — the HUD's banner only says "Level N"
// and who): who reached which level, health/mana gained, talent points, new ranks at
// the trainer (or learned for free by companions); merges simultaneous level-ups, dismissable, fades after a while.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class GameOverScreen : IUiScreen
    {
        public string Id => "";
        public int Order => 340;
        public bool Modal => true;

        public bool Visible
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && s.Mode == SessionMode.GameOver;
            }
        }

        float shownAt;
        bool wasVisible, listing;
        SaveSlotInfo latest;
        string latestText = "", latestButton = "Load last save";
        readonly SaveSlotsView loadView = new SaveSlotsView { SaveMode = false };

        struct Ember
        {
            public Vector2 Pos;
            public float Speed, Size, Phase;
        }

        readonly Ember[] embers = new Ember[40];
        readonly System.Random rng = new System.Random(77);

        public void Tick(float dt)
        {
            bool vis = Visible;
            if (vis && !wasVisible)
            {
                shownAt = Time.unscaledTime;
                listing = false;
                loadView.Invalidate();
                try { latest = PanelKit.Flow?.LatestSave(); } catch (Exception) { latest = null; }
                latestText = latest != null && latest.Header != null
                    ? $"{PanelKit.SlotDisplay(latest.Slot)} · {latest.Header.playerName}, level {latest.Header.playerLevel} · {latest.Modified:MMM d, HH:mm}"
                    : "";
                latestButton = latestText.Length > 0 ? "Load last save\n<size=15>" + latestText + "</size>" : "Load last save";
                for (int i = 0; i < embers.Length; i++) embers[i] = NewEmber(true);
            }
            wasVisible = vis;
            if (!vis) return;
            for (int i = 0; i < embers.Length; i++)
            {
                var e = embers[i];
                e.Pos.y += e.Speed * dt;
                e.Pos.x += Mathf.Sin(Time.unscaledTime * 0.7f + e.Phase) * 0.01f * dt;
                if (e.Pos.y > 1.05f) e = NewEmber(false);
                embers[i] = e;
            }
        }

        Ember NewEmber(bool anywhere) => new Ember
        {
            Pos = new Vector2((float)rng.NextDouble(), anywhere ? (float)rng.NextDouble() : -0.05f),
            Speed = 0.02f + (float)rng.NextDouble() * 0.03f,
            Size = 4f + (float)rng.NextDouble() * 8f,
            Phase = (float)rng.NextDouble() * 6.28f,
        };

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                PanelKit.OccludeAll(Order);
                Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
                float t = Time.unscaledTime - shownAt;
                float a = Mathf.Clamp01(t / 2.2f);
                float W = Ui.Width, H = Ui.Height;
                if (PanelKit.IsRepaint)
                {
                    PanelKit.Rect(new Rect(0f, 0f, W, H), new Color(0.07f, 0.05f, 0.12f, 0.78f * a));
                    var vig = ArtLibrary.HasRealTexture("ui_vignette") ? ArtLibrary.Texture("ui_vignette") : null;
                    if (vig != null) PanelKit.Tex(new Rect(0f, 0f, W, H), vig, new Color(1f, 1f, 1f, a));
                    for (int i = 0; i < embers.Length; i++)
                    {
                        var e = embers[i];
                        float tw = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.7f + e.Phase * 2f);
                        var p = new Vector2(e.Pos.x * W, e.Pos.y * H);
                        PanelKit.Tex(new Rect(p.x - e.Size * 1.5f, p.y - e.Size * 1.5f, e.Size * 3f, e.Size * 3f), ProceduralArt.Glow, new Color(0.75f, 0.6f, 0.95f, 0.25f * tw * a));
                        PanelKit.Tex(new Rect(p.x - e.Size * 0.2f, p.y - e.Size * 0.2f, e.Size * 0.4f, e.Size * 0.4f), PanelArt.Disc, new Color(0.9f, 0.85f, 1f, 0.6f * tw * a));
                    }
                }
                float ta = Mathf.Clamp01((t - 0.6f) / 1.4f);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, ta);
                if (!listing)
                {
                    Ui.Shadowed(new Rect(0f, H * 0.25f, W, 80f), "The lanterns dim…", PanelKit.LTitle);
                    PanelKit.Label(new Rect(0f, H * 0.25f + 82f, W, 34f), "Your party has fallen. The Hollow creeps a little further into the valley.", PanelKit.LTextCenter, new Color(1f, 0.93f, 0.85f, 0.85f));
                    float bw = 420f, x = (W - bw) * 0.5f, y = H * 0.25f + 160f;
                    bool canLoad = latest != null && latest.Header != null;
                    string label = canLoad ? latestButton : "Load last save";
                    if (Ui.Btn(new Rect(x, y, bw, canLoad ? 76f : 58f), label, Ui.ButtonGold, canLoad && ta > 0.5f, canLoad ? null : "No save found."))
                    {
                        string slot = latest.Slot;
                        PanelKit.Do(() => PanelKit.Try(() => PanelKit.Flow != null ? PanelKit.Flow.LoadFromSlot(slot) : "Not ready."));
                    }
                    y += (canLoad ? 76f : 58f) + 14f;
                    if (Ui.Btn(new Rect(x, y, bw, 58f), "Load a save…", Ui.Button, ta > 0.5f)) { listing = true; loadView.Invalidate(); }
                    y += 72f;
                    if (Ui.Btn(new Rect(x, y, bw, 58f), "Main Menu", Ui.Button, ta > 0.5f)) PanelKit.Do(() => PanelKit.Flow?.ReturnToMainMenu());
                }
                else
                {
                    var r = PanelKit.Centered(Mathf.Min(980f, W - 40f), Mathf.Min(760f, H - 80f));
                    var c = PanelKit.Window(r, "Load Game", Order, out bool close);
                    if (close) listing = false;
                    loadView.Draw(new Rect(c.x, c.y, c.width, c.height - 64f));
                    if (Ui.Btn(new Rect(c.x, c.yMax - 50f, 160f, 48f), "Back")) listing = false;
                }
                GUI.color = old;
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }

    // ==================================================================================== level up

    public sealed class LevelUpPopup : IUiScreen
    {
        public string Id => "";
        public int Order => 160;
        public bool Modal => false;
        public bool Visible => entries.Count > 0 && GameFlow.HasGame && PanelKit.Mode != SessionMode.Dialogue && PanelKit.Mode != SessionMode.GameOver;

        sealed class Entry
        {
            public Unit Unit;
            public int FromLevel, Level;
            public float Health, Mana;
            public int Talents;
            public readonly List<string> Trainable = new List<string>();
            public readonly List<string> Learned = new List<string>();
            public string Text = "";
        }

        readonly List<Entry> entries = new List<Entry>();
        float shownAt, lastEventAt;
        bool hovered;
        const float Life = 11f;
        static LevelUpPopup instance;

        public LevelUpPopup()
        {
            instance = this;
            PanelKit.Events += OnEvent;
        }

        static LevelUpPopup()
        {
            EscRouter.Register(500, () =>
            {
                var p = instance;
                if (p == null || !p.Visible) return false;
                p.entries.Clear();
                return true;
            });
        }

        Entry For(Unit u)
        {
            foreach (var e in entries) if (e.Unit == u) return e;
            var n = new Entry { Unit = u };
            entries.Add(n);
            return n;
        }

        void OnEvent(SessionEvent e)
        {
            switch (e.Kind)
            {
                case SessionEventKind.LevelUp:
                {
                    var u = e.Unit ?? e.LevelUp?.Unit;
                    if (u == null) return;
                    if (Time.unscaledTime - lastEventAt > 2f && Time.unscaledTime - shownAt > Life) entries.Clear();
                    var en = For(u);
                    var info = e.LevelUp;
                    if (en.FromLevel == 0) en.FromLevel = info != null ? info.OldLevel : Mathf.Max(1, e.Amount - 1);
                    en.Level = Mathf.Max(en.Level, e.Amount > 0 ? e.Amount : (info != null ? info.NewLevel : u.Level));
                    if (info != null)
                    {
                        en.Health += info.HealthGained;
                        en.Mana += info.ManaGained;
                        en.Talents += info.TalentPointsGained;
                        foreach (var o in info.NewTrainable)
                        {
                            if (o == null || o.Ability == null) continue;
                            string t = o.Ability.name + (AbilityRules.RankCount(o.Ability) > 1 ? " " + o.Rank : "");
                            if (!en.Trainable.Contains(t)) en.Trainable.Add(t);
                        }
                    }
                    Touch(en);
                    break;
                }
                case SessionEventKind.AbilityLearned:
                {
                    var u = e.Unit;
                    if (u == null || u == PanelKit.Sess?.Main) return;
                    if (Time.unscaledTime - lastEventAt > 1.5f) return;   // only alongside a level-up (companions auto-train)
                    Entry en = null;
                    foreach (var x in entries) if (x.Unit == u) en = x;
                    if (en == null) return;
                    var a = PanelKit.Db != null ? PanelKit.Db.Ability(e.Id) : null;
                    string t = (a != null ? a.name : e.Id) + (a != null && AbilityRules.RankCount(a) > 1 ? " " + e.Amount : "");
                    if (!en.Learned.Contains(t)) en.Learned.Add(t);
                    Touch(en);
                    break;
                }
                case SessionEventKind.GameStarted:
                case SessionEventKind.GameLoaded:
                    entries.Clear();
                    break;
            }
        }

        void Touch(Entry en)
        {
            lastEventAt = Time.unscaledTime;
            shownAt = Time.unscaledTime;
            en.Text = Describe(en);
        }

        static string Describe(Entry en)
        {
            var sb = new StringBuilder();
            var u = en.Unit;
            bool comp = u.Companion != null;
            sb.Append("<b>").Append(PanelKit.NameOf(u)).Append("</b> reached level <b>").Append(en.Level).Append("</b>");
            var parts = new List<string>();
            if (en.Health > 0.5f) parts.Add("+" + Mathf.RoundToInt(en.Health) + " health");
            if (en.Mana > 0.5f) parts.Add("+" + Mathf.RoundToInt(en.Mana) + " mana");
            if (parts.Count > 0) sb.Append("\n").Append(string.Join(" · ", parts));
            if (en.Talents > 0)
            {
                bool auto = comp && PanelKit.Sess != null && PanelKit.Sess.Settings.AutoAllocateCompanionTalents;
                sb.Append("\n").Append(Ui.Rich(en.Talents == 1 ? "1 talent point" : en.Talents + " talent points", PanelKit.GoodDark));
                sb.Append(auto ? " (spent on their build)" : " — open Talents (N)");
            }
            if (en.Learned.Count > 0) sb.Append("\nLearned: ").Append(Join(en.Learned));
            else if (en.Trainable.Count > 0) sb.Append("\nNew at the trainer: ").Append(Join(en.Trainable));
            return sb.ToString();
        }

        float CardHeight(int shown, float w)
        {
            float h = 82f + 56f;
            for (int i = 0; i < shown; i++) h += Mathf.Max(64f, PanelKit.TextHeight(entries[i].Text, PanelKit.TextSmall, w - 120f) + 12f) + 8f;
            return h;
        }

        static string Join(List<string> l)
        {
            if (l.Count <= 4) return string.Join(", ", l);
            return string.Join(", ", l.GetRange(0, 4)) + $" and {l.Count - 4} more";
        }

        public void Tick(float dt)
        {
            if (entries.Count == 0) return;
            if (!Visible) { shownAt = Time.unscaledTime; return; }   // wait for the conversation to end
            if (!hovered && Time.unscaledTime - shownAt > Life) entries.Clear();
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                float t = Time.unscaledTime - shownAt;
                float a = Mathf.Clamp01(t / 0.3f) * (hovered ? 1f : Mathf.Clamp01((Life - t) / 0.8f));
                const float w = 500f;
                int shown = Mathf.Min(entries.Count, 4);
                // bottom right, apart from the HUD's short "Level N" banner and the toast lane (top centre): the card never
                // rises above the lower 62 % of the screen — with many entries (or a large interface size) it shows fewer
                float top = Ui.Height * 0.38f;
                float h = CardHeight(shown, w);
                while (shown > 1 && Ui.Height - h - 120f < top) h = CardHeight(--shown, w);
                float slide = (1f - Mathf.Clamp01(t / 0.3f)) * 30f;
                var r = new Rect(Ui.Width - w - 26f + slide, Mathf.Max(8f, Ui.Height - h - 120f), w, h);
                // a victory opens the loot window at the same moment (Order 150, below this card): move left of it so
                // its item rows and Close button stay reachable
                if (LootScreen.TryGetWindowRect(out var loot) && r.Overlaps(loot))
                    r.x = Mathf.Max(8f, loot.x - w - 16f + slide);
                hovered = PanelKit.Hover(r);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                PanelKit.Tex(new Rect(r.x - 30f, r.y - 40f, r.width + 60f, 150f), ProceduralArt.Glow, new Color(1f, 0.82f, 0.4f, 0.4f * a));
                Ui.Panel(r);
                PanelKit.Occlude(r, Order);
                PanelKit.Label(new Rect(r.x, r.y + 14f, r.width, 48f), "Level up!", PanelKit.TitleCenterDark, PanelKit.GoldInk);
                for (int i = 0; i < 6; i++)
                {
                    float ang = Time.unscaledTime * 0.8f + i * 1.047f;
                    var p = new Vector2(r.center.x + Mathf.Cos(ang) * 130f, r.y + 38f + Mathf.Sin(ang * 1.3f) * 10f);
                    PanelKit.Tex(new Rect(p.x - 8f, p.y - 8f, 16f, 16f), PanelArt.Sparkle, new Color(1f, 0.82f, 0.4f, 0.8f * a));
                }
                float y = r.y + 74f;
                for (int i = 0; i < shown; i++)
                {
                    var en = entries[i];
                    float rh = Mathf.Max(64f, PanelKit.TextHeight(en.Text, PanelKit.TextSmall, w - 120f) + 12f);
                    Ui.Portrait(new Rect(r.x + 24f, y, 60f, 60f), PanelKit.PortraitOf(en.Unit), PanelKit.ColorOf(en.Unit));
                    PanelKit.Label(new Rect(r.x + 98f, y + 2f, w - 120f, rh), en.Text, PanelKit.TextSmall);
                    y += rh + 8f;
                }
                bool talents = false;
                foreach (var en in entries) if (en.Talents > 0) talents = true;
                if (talents && Ui.Btn(new Rect(r.x + 24f, r.yMax - 58f, 200f, 42f), "Talents (N)", PanelKit.SmallButtonGold))
                {
                    Entry pick = null;
                    foreach (var en in entries) if (en.Talents > 0 && (pick == null || en.Unit == PanelKit.Sess?.Main)) pick = en;
                    if (pick != null) { PanelKit.Member = pick.Unit; TalentsPanel.SelectMember(pick.Unit); }
                    UiRoot.Open(UiPanels.Talents);
                    entries.Clear();
                }
                if (Ui.Btn(new Rect(r.xMax - 144f, r.yMax - 58f, 120f, 42f), "Close", PanelKit.SmallButton)) entries.Clear();
                GUI.color = old;
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }
    }
}
