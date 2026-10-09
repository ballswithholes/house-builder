// Raid party picker (Order 212, modal): opened by GameFlow when the session raises RaidPartyRequested (walking, clicking or
// teleporting into a raid map without a raid party; Id = map, Id2 = spawn, Amount = raidSize). Lists
// GameSession.RaidCandidates() — the main character (locked in, leads the raid), the party you travel with, then the
// companions at camp — with portrait, class colour, role, level and status, a red warning below the map's levelMin, check
// boxes up to raidSize, the role summary ("2 tanks · 2 healers · 6 dps") with advice when the raid lacks a tank or a healer,
// and "Companions auto-play in this raid" (on by default). Enter the raid → GameSession.EnterRaid(map, spawn, ids, autoPlay)
// (the party walks through; leaving the raid brings the old party back); Cancel / Esc / × → stay where you are.
// The suggestion (RaidPlanning.DefaultSelection) keeps the party you travel with and fills the raid from camp, tanks and
// healers first; "Suggest" puts it back.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class RaidPickerScreen : IUiScreen
    {
        public const int ScreenOrder = 212;
        public string Id => "";
        public int Order => ScreenOrder;
        public bool Modal => Visible;

        public bool Visible
        {
            get
            {
                if (!open || !GameFlow.HasGame) return false;
                var s = PanelKit.Sess;
                return s != null && s == session && s.Mode == SessionMode.Exploration;
            }
        }

        // ------------------------------------------------------------ request (static: GameFlow opens it)
        static bool open, resetSelection, dirty, entering;
        static string mapId = "", spawnId = "";
        static int raidSize;
        static GameSession session;
        static int openedFrame;
        static RaidPickerScreen instance;

        /// <summary>True while the picker is open (it may be hidden while the session is not exploring).</summary>
        public static bool IsOpen => open;
        /// <summary>The raid map the picker is choosing a party for ("" when closed).</summary>
        public static string MapId => open ? mapId : "";

        /// <summary>Opens the picker for a raid map (GameFlow, on SessionEventKind.RaidPartyRequested).</summary>
        public static void Open(string map, string spawn, int size)
        {
            var s = PanelKit.Sess;
            if (s == null || string.IsNullOrEmpty(map)) return;
            bool same = open && session == s && mapId == map;
            mapId = map;
            spawnId = string.IsNullOrEmpty(spawn) ? "default" : spawn;
            raidSize = size;
            session = s;
            dirty = true;
            if (same) return;   // asked again (walked out and back in): keep the choice being made
            open = true;
            entering = false;
            resetSelection = true;
            autoPlay = true;    // the default every time: ten manual turns a round is too many
            openedFrame = Time.frameCount;
            ContextMenuScreen.Close();
            Ui.Sfx?.Invoke("ui_open");
        }

        public static void Close()
        {
            if (!open) return;
            open = false;
            session = null;
            mapId = spawnId = "";
            Ui.Sfx?.Invoke("ui_close");
        }

        static RaidPickerScreen()
        {
            EscRouter.Register(850, () =>
            {
                var p = instance;
                if (!open || p == null || !p.Visible) return false;
                Close();
                return true;
            });
        }

        public RaidPickerScreen()
        {
            instance = this;
            PanelKit.Events += e =>
            {
                switch (e.Kind)
                {
                    case SessionEventKind.GameStarted:
                    case SessionEventKind.GameLoaded:
                    case SessionEventKind.GameOver:
                        open = false;
                        session = null;
                        break;
                    case SessionEventKind.PartyChanged:
                    case SessionEventKind.CompanionRecruited:
                    case SessionEventKind.CompanionDismissed:
                    case SessionEventKind.LevelUp:
                        dirty = true;
                        break;
                    case SessionEventKind.RaidStarted:
                        open = false;   // entered (by this picker or anything else)
                        session = null;
                        break;
                }
            };
        }

        // ------------------------------------------------------------ candidates
        sealed class Cand
        {
            public Unit Unit;
            public string Id = "", Name = "", Line = "", Status = "", Warning, Portrait = "";
            public UnitRole Role;
            public bool Locked, InParty;
            public Color Ink;
        }

        readonly List<Cand> cands = new List<Cand>();
        readonly List<string> chosen = new List<string>();
        readonly Dictionary<Unit, UnitRole> roles = new Dictionary<Unit, UnitRole>();
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        static bool autoPlay = true;
        string intro = "", bandWarning, summary = "", advice, countText = "", enterWhy;
        MapDef map;

        public void Tick(float dt)
        {
            if (!open) return;
            var s = PanelKit.Sess;
            if (s == null || s != session || !GameFlow.HasGame || s.InRaid || s.IsGameOver || !s.Db.Maps.ContainsKey(mapId))
            {
                open = false;
                session = null;
                return;
            }
            if (dirty) Rebuild(s);
            if (Visible && !ConfirmScreen.IsOpen && Time.frameCount > openedFrame + 1 && PanelKit.ConfirmKeyDown() && enterWhy == null)
            {
                UiRoot.HotkeysSuppressed = true;
                Enter();
            }
        }

        UnitRole RoleOf(Unit u)
        {
            if (u == null) return UnitRole.MeleeDps;
            if (roles.TryGetValue(u, out var r)) return r;
            try { r = u.Role; } catch (Exception) { r = UnitRole.MeleeDps; }
            roles[u] = r;
            return r;
        }

        void Rebuild(GameSession s)
        {
            dirty = false;
            roles.Clear();
            map = s.Db.Maps.TryGetValue(mapId, out var m) ? m : null;
            var list = s.RaidCandidates();
            cands.Clear();
            int levelMin = map != null ? map.levelMin : 0;
            foreach (var u in list)
            {
                if (u == null) continue;
                var role = RoleOf(u);
                bool main = u == s.Main;
                bool inParty = s.IsInParty(u);
                cands.Add(new Cand
                {
                    Unit = u, Id = s.MemberId(u), Name = PanelKit.NameOf(u), Role = role, Locked = main, InParty = inParty,
                    Line = $"Level {u.Level} {(u.Class != null ? u.Class.name : "")} · {RoleName(role)}",
                    Status = main ? "Leads the raid" : inParty ? "Travelling with you" : "Waiting at camp",
                    Warning = RaidPlanning.LevelWarning(u.Level, levelMin),
                    Portrait = PanelKit.PortraitOf(u),
                    Ink = u.Class != null ? PanelKit.InkColorOf(u.Class.id) : Ui.Ink,
                });
            }
            if (resetSelection)
            {
                resetSelection = false;
                Suggest(s, list);
            }
            else
            {
                // keep the choice; drop whoever is no longer a candidate (dismissed meanwhile); Main is always in
                chosen.RemoveAll(id => cands.Find(c => c.Id == id) == null);
                if (!chosen.Contains(GameSession.MainId)) chosen.Insert(0, GameSession.MainId);
                while (chosen.Count > Mathf.Max(1, raidSize)) chosen.RemoveAt(chosen.Count - 1);
            }
            string name = map != null && !string.IsNullOrEmpty(map.name) ? map.name : mapId;
            string band = RaidPlanning.BandText(map);
            intro = $"Choose up to <b>{raidSize}</b> heroes for {name}{(band.Length > 0 ? " (" + band + ")" : "")}. {PanelKit.NameOf(s.Main)} leads the raid. Companions you leave behind wait for you outside.";
            bandWarning = s.Main != null && levelMin > 0 && s.Main.Level < levelMin
                ? $"Your party is level {s.Main.Level}: this raid is meant for level {levelMin} and up."
                : null;
            Recount(s);
        }

        void Suggest(GameSession s, IReadOnlyList<Unit> list)
        {
            chosen.Clear();
            chosen.AddRange(RaidPlanning.DefaultSelection(s, list, raidSize, RoleOf));
        }

        void Recount(GameSession s)
        {
            var c = new RaidPlanning.RoleCounts();
            foreach (var cand in cands) if (chosen.Contains(cand.Id)) c.Add(cand.Role);
            summary = RaidPlanning.RoleSummary(c);
            advice = RaidPlanning.RoleAdvice(c);
            countText = $"<b>{chosen.Count} / {raidSize}</b> chosen";
            try { enterWhy = s.CannotEnterRaidReason(mapId, chosen); }
            catch (Exception e) { Debug.LogException(e); enterWhy = "The raid cannot be entered right now."; }
        }

        static string RoleName(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Tank: return "Tank";
                case UnitRole.Healer: return "Healer";
                case UnitRole.RangedDps: return "Ranged damage";
                default: return "Melee damage";
            }
        }

        static string RoleGlyph(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Tank: return "glyph_shield";
                case UnitRole.Healer: return "glyph_heal_plus";
                case UnitRole.RangedDps: return "glyph_bow";
                default: return "glyph_sword";
            }
        }

        static Color RoleColor(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Tank: return Ui.Hex("#4f7bd0");
                case UnitRole.Healer: return Ui.Hex("#3f9a52");
                default: return Ui.Hex("#b5473c");
            }
        }

        // ------------------------------------------------------------ commands

        void Toggle(Cand c)
        {
            if (c == null || c.Locked) return;
            var s = PanelKit.Sess;
            if (s == null) return;
            if (chosen.Contains(c.Id)) chosen.Remove(c.Id);
            else if (chosen.Count >= raidSize) { PanelKit.Notice($"The raid takes at most {raidSize}. Leave someone out first."); return; }
            else chosen.Add(c.Id);
            Ui.Sfx?.Invoke("ui_click");
            Recount(s);
        }

        void Enter()
        {
            var s = PanelKit.Sess;
            if (s == null || s != session || entering) return;
            entering = true;   // one EnterRaid per press (it runs next Update)
            var ids = new List<string>(chosen);
            string map = mapId, spawn = spawnId;
            bool auto = autoPlay;
            PanelKit.Do(() =>
            {
                entering = false;
                var ss = PanelKit.Sess;
                if (ss == null || ss != s || !open) return;
                string why;
                try { why = ss.EnterRaid(map, spawn, ids, auto); }
                catch (Exception e) { Debug.LogException(e); why = "The raid could not be entered."; }
                if (why != null) { PanelKit.Notice(why); dirty = true; return; }
                Close();
            });
        }

        // ------------------------------------------------------------ draw

        const float RowH = 76f, ColGap = 12f, FooterH = 132f, IntroH = 64f;

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var s = PanelKit.Sess;
            if (s == null) return;
            if (dirty) Rebuild(s);   // opened after this frame's Tick
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                if (Time.frameCount <= openedFrame + 1) PanelKit.SwallowMouseDown(new Rect(0f, 0f, Ui.Width, Ui.Height));
                PanelKit.OccludeAll(Order);
                PanelKit.Rect(new Rect(0f, 0f, Ui.Width, Ui.Height), new Color(0.06f, 0.04f, 0.10f, 0.4f));
                int rows = (cands.Count + 1) / 2;
                float w = Mathf.Min(960f, Ui.Width - 40f);
                float h = 74f + IntroH + rows * RowH + 8f + FooterH + 18f;
                var r = PanelKit.Centered(w, h);
                string title = map != null && !string.IsNullOrEmpty(map.name) ? map.name : mapId;
                var c = PanelKit.Window(r, "Raid party", Order, out bool close, title);
                if (close) { Close(); return; }

                // intro + the party-level warning
                PanelKit.Label(new Rect(c.x, c.y - 4f, c.width, 44f), intro, PanelKit.TextSmall);
                if (bandWarning != null)
                    PanelKit.Label(new Rect(c.x, c.y + 38f, c.width, 22f), bandWarning, PanelKit.TextSmall, PanelKit.BadDark);

                // candidates (two columns, scrolls when the window is short)
                var list = new Rect(c.x, c.y + IntroH, c.width, Mathf.Max(RowH, c.height - IntroH - FooterH));
                float cw = PanelKit.BeginScroll(list, scroll, rows * RowH);
                try
                {
                    float colW = (cw - ColGap) * 0.5f;
                    for (int i = 0; i < cands.Count; i++)
                    {
                        var cell = new Rect((i % 2) * (colW + ColGap), (i / 2) * RowH, colW, RowH - 8f);
                        if (!scroll.IsVisible(cell)) continue;
                        DrawCandidate(cell, cands[i]);
                    }
                }
                finally { PanelKit.EndScroll(scroll); }

                // footer: count + role summary (+ advice), auto-play, buttons
                float fy = list.yMax + 10f;
                PanelKit.Label(new Rect(c.x, fy, 200f, 30f), countText, PanelKit.Text);
                var sr = new Rect(c.x + 190f, fy, c.width - 190f, 30f);
                PanelKit.Label(sr, summary, PanelKit.TextBold, Ui.Ink);
                Ui.TooltipFor(sr, "Tanks hold the bosses, healers keep everyone standing, damage dealers bring them down. " +
                                  $"A raid of {raidSize} usually takes {RaidPlanning.WantedTanks(raidSize)} tanks and {RaidPlanning.WantedHealers(raidSize)} or more healers.");
                if (advice != null) PanelKit.Label(new Rect(c.x, fy + 30f, c.width, 24f), advice, PanelKit.TextSmall, PanelKit.BadDark);

                float by = r.yMax - 66f;
                bool nv = SettingsView.Toggle(new Rect(c.x, by + 6f, Mathf.Min(430f, c.width - 470f), 34f), "Companions auto-play in this raid", autoPlay,
                    "The companions' AI plays their turns in the raid's fights (you keep your own character; the AUTO pills and the combat bar's toggles change it). " +
                    "Leaving the raid puts everyone's auto-play back as it was.");
                if (nv != autoPlay) autoPlay = nv;
                float bx = c.xMax;
                bx -= 200f;
                if (PanelKit.PressButton(new Rect(bx, by, 200f, 48f), "Enter the raid", Ui.ButtonGold, enterWhy == null, enterWhy ?? "Enter with the chosen heroes (Enter)."))
                    Enter();
                bx -= 138f;
                if (PanelKit.PressButton(new Rect(bx, by, 130f, 48f), "Cancel", Ui.Button, true, "Stay where you are (Esc)."))
                    Close();
                bx -= 118f;
                if (PanelKit.PressButton(new Rect(bx, by + 6f, 110f, 38f), "Suggest", PanelKit.SmallButton, true,
                        "Pick for me: the party you travel with, then tanks and healers from camp, then the rest."))
                {
                    Suggest(s, s.RaidCandidates());
                    Recount(s);
                }
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        void DrawCandidate(Rect cell, Cand cd)
        {
            bool on = chosen.Contains(cd.Id);
            bool full = !on && chosen.Count >= raidSize;
            bool hover = PanelKit.Hover(cell);
            PanelKit.Rounded(cell, on ? PanelKit.RowSelected : hover && !full ? PanelKit.RowHover : PanelKit.RowShade);
            if (cd.Warning != null) PanelKit.Outline(cell, new Color(PanelKit.BadDark.r, PanelKit.BadDark.g, PanelKit.BadDark.b, 0.55f));

            // check box (Main: locked in)
            var box = new Rect(cell.x + 10f, cell.y + (cell.height - 26f) * 0.5f, 26f, 26f);
            PanelKit.Rounded(box, cd.Locked ? Ui.PaperShade : hover && !full ? Ui.Hex("#ffe9b8") : Color.white);
            PanelKit.Outline(box, cd.Locked ? Ui.InkSoft : Ui.Ink);
            if (on) PanelKit.Tex(new Rect(box.x + 3f, box.y + 3f, 20f, 20f), PanelArt.Check, cd.Locked ? Ui.InkSoft : PanelKit.GoodDark);

            // portrait + role badge
            var pr = new Rect(box.xMax + 10f, cell.y + 6f, cell.height - 12f, cell.height - 12f);
            Ui.Portrait(pr, cd.Portrait, PanelKit.ColorOf(cd.Unit), !on);
            var rb = new Rect(pr.xMax - 20f, pr.yMax - 20f, 22f, 22f);
            PanelKit.Rounded(rb, RoleColor(cd.Role));
            var glyph = Ui.GlyphTexture(RoleGlyph(cd.Role));
            if (glyph != null) PanelKit.Tex(new Rect(rb.x + 3f, rb.y + 3f, 16f, 16f), glyph, Color.white, ScaleMode.ScaleToFit);

            // name, level · class · role, status / level warning
            float tx = pr.xMax + 12f, tw = cell.xMax - tx - 8f;
            PanelKit.Label(new Rect(tx, cell.y + 4f, tw, 24f), cd.Name, PanelKit.RowText, cd.Ink);
            PanelKit.Label(new Rect(tx, cell.y + 26f, tw, 20f), cd.Line, PanelKit.RowTextSmall);
            if (cd.Warning != null) PanelKit.Label(new Rect(tx, cell.y + 46f, tw, 20f), cd.Warning, PanelKit.RowTextSmall, PanelKit.BadDark);
            else PanelKit.Label(new Rect(tx, cell.y + 46f, tw, 20f), cd.Status, PanelKit.RowTextSmall, cd.Locked ? PanelKit.GoldInk : Ui.InkSoft);

            string tip = cd.Locked ? $"{cd.Name} leads the raid and always comes along."
                       : full ? $"The raid takes at most {raidSize}. Leave someone out first."
                       : on ? "Click to leave them out." : "Click to bring them along.";
            if (cd.Unit != null && cd.Unit.Companion != null) tip += "\n\n" + CharacterPanel.CompanionAbout(cd.Unit.Companion);
            Ui.TooltipFor(cell, tip);
            GameInput.BlockRectGui(cell);
            if (PanelKit.LeftClick(cell))
            {
                if (cd.Locked) return;
                Toggle(cd);
            }
        }
    }
}
