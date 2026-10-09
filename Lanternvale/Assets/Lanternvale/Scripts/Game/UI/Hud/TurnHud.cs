// Turn economy (combat): the 6.0 s Time bar in four 1.5 s GCD pips with the hovered (or being-targeted) ability's cost previewed
// (overflow → time debt for instants, "pending" for casts), the time debt carried into the next turn, movement
// left in metres, End Turn (Space), Leave Fight (practice fights), fast-forward, and a status pill: targeting
// hints, "Grey Wolf is acting…" during AI turns (with "Take control" for auto-played companions), pending casts.
// Raids and big battles add two toggles above End Turn: "Auto: all companions" and "Auto-battle" (the whole party, the main
// character too); turning one off puts back every per-unit auto-play flag it changed. Fast animations default on in raids
// (a separate remembered choice from ordinary fights).
// The combat controller's HoverPreview follows the cursor. SelfResPromptHud offers Soulstone/Reincarnation.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class TurnEconomyHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderTurn;
        public bool Visible => Hud.CombatHud;
        public bool Modal => false;

        const float Seg = 1.5f, TurnSeconds = 6f;
        const float PreviewMaxText = 620f, PreviewPadX = 10f, PreviewPadY = 5f;

        readonly HudText.Tenths timeText = new HudText.Tenths("", " s");
        readonly HudText.Tenths debtText = new HudText.Tenths("", " s next turn");
        readonly HudText.Tenths moveText = new HudText.Tenths("", " m");
        readonly HudText.Tenths budgetText = new HudText.Tenths("/ ", " m");
        readonly HudText.Tenths overflowText = new HudText.Tenths("+", " s debt");
        readonly GUIContent measure = new GUIContent();
        string measuredText;
        int measuredStyles = -1;
        Vector2 measuredSize;

        Unit statusUnit;
        string statusText = "";
        int statusKind = -1;

        // the fast-animation toggle is remembered across fights (each battle gets a new CombatController); raids keep their
        // own choice, on until the player turns it off there (a raid round is ten or more turns per side)
        const string FastForwardKey = "lv.hud.fastForward", FastForwardRaidKey = "lv.hud.fastForwardRaid";
        static int fastForwardPref = -1, fastForwardRaidPref = -1;   // -1 = not loaded
        CombatController ffApplied;

        static bool FastForwardPref
        {
            get => LoadPref(FastForwardKey, ref fastForwardPref, 0);
            set => SavePref(FastForwardKey, ref fastForwardPref, value);
        }

        static bool FastForwardRaidPref
        {
            get => LoadPref(FastForwardRaidKey, ref fastForwardRaidPref, 1);
            set => SavePref(FastForwardRaidKey, ref fastForwardRaidPref, value);
        }

        static bool LoadPref(string key, ref int cache, int fallback)
        {
            if (cache < 0)
            {
                try { cache = PlayerPrefs.GetInt(key, fallback) == 1 ? 1 : 0; } catch (Exception) { cache = fallback; }
            }
            return cache == 1;
        }

        static void SavePref(string key, ref int cache, bool value)
        {
            cache = value ? 1 : 0;
            try { PlayerPrefs.SetInt(key, cache); } catch (Exception) { }
        }

        static bool InRaid { get { var s = Hud.Session; return s != null && s.InRaid; } }

        public void Tick(float dt)
        {
            var s = Hud.Session;
            if (s != snapSession || (s != null && s.InRaid) != snapInRaid)
            {
                // another game, or the raid began or ended (EndRaidParty restored the party's own flags): stale snapshots
                snapSession = s;
                snapInRaid = s != null && s.InRaid;
                companionsSnap = battleSnap = null;
            }
            var c = Hud.Combat;
            if (c == ffApplied) return;
            ffApplied = c;
            if (c != null && (InRaid ? FastForwardRaidPref : FastForwardPref)) c.FastForward = true;
        }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("turn:" + e.GetType().Name, "Turn HUD: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner()
        {
            HudStyles.Ensure();
            var c = Hud.Combat;
            var b = c != null ? c.Battle : null;
            if (b == null) return;
            var turnUnit = Hud.ActivePlayerUnit;
            var ai = Hud.ActiveAiUnit;

            DrawStatus(c, b, turnUnit, ai);
            if (turnUnit != null && !b.IsOver) DrawTurnRow(c, turnUnit);
            DrawButtons(c, b, turnUnit);
            DrawAutoToggles(b);
            DrawCursorPreview(c);
        }

        // ================================================================ status pill

        void DrawStatus(CombatController c, Battle b, Unit turnUnit, Unit ai)
        {
            if (Hud.FieldPick != null || b.IsOver) return;
            var r = HudLayout.StatusPill;
            if (c.IsTargeting)
            {
                string name = c.TargetingItem != null ? c.TargetingItem.Name : (c.TargetingAbility != null ? c.TargetingAbility.name : "");
                string text = Status(0, null, name);
                Pill(r, text, Ui.Gold, null);
                return;
            }
            if (ai != null)
            {
                string text = Status(1, ai, null);
                bool companion = ai.Team == b.PlayerTeam && (ai.AutoPlay || (ai.Owner != null && ai.Owner.AutoPlay));
                Pill(r, text, ai.Team == b.PlayerTeam ? Hud.PartyTeam : Hud.EnemyTeam, "glyph_hourglass");
                if (companion)
                {
                    var owner = ai.AutoPlay ? ai : ai.Owner;
                    var br = new Rect(r.xMax - 132f, r.y + 4f, 124f, r.height - 8f);
                    if (takeControlFor != owner) { takeControlFor = owner; takeControlTip = "Turn auto-play off for " + Hud.NameOf(owner) + " (from its next decision)."; }
                    if (Ui.Btn(br, "Take control", HudStyles.Button, true, takeControlTip))
                        Hud.Post(() => Hud.Combat?.SetAutoPlay(owner, false));
                }
                return;
            }
            // the selected/inspected party member is busy casting across turns
            var sel = Hud.Flow != null ? Hud.Flow.Selected : null;
            var caster = turnUnit == null && sel != null && sel.Pending != null ? sel : null;
            if (caster != null && caster.Pending.Ability != null)
            {
                Pill(r, Status(2, caster, caster.Pending.Ability.name), Hud.SchoolCol(caster.Pending.Ability.school), "glyph_hourglass");
                return;
            }
            // quiet "whose turn" line
            if (turnUnit != null)
                HudDraw.Text(new Rect(r.x, r.y + 4f, r.width, r.height - 4f), Status(3, turnUnit, null), HudStyles.Center, Hud.UnitColor(turnUnit));
        }

        string Status(int kind, Unit u, string name)
        {
            // cached per (kind, unit, name, dots) to avoid rebuilding every event
            string dots = HudText.Dots();
            if (kind != 1) dots = "";
            if (kind == statusKind && u == statusUnit && statusText.Length > 0 && statusCacheName == name && statusDots == dots) return statusText;
            statusKind = kind;
            statusUnit = u;
            statusCacheName = name;
            statusDots = dots;
            switch (kind)
            {
                case 0: statusText = "Choose a target for " + name + "  ·  left-click to confirm  ·  right-click or Esc to cancel"; break;
                case 1: statusText = Hud.NameOf(u) + (u.AutoPlay ? " (auto-play)" : "") + " is acting" + dots; break;
                case 3: statusText = Hud.NameOf(u) + "'s turn"; break;
                default: statusText = Hud.NameOf(u) + " is casting " + name + " — it resolves at the start of their next turn"; break;
            }
            return statusText;
        }
        string statusCacheName, statusDots;
        Unit takeControlFor, endTipFor;
        string endTip = "";
        string takeControlTip = "";

        static void Pill(Rect r, string text, Color edge, string glyph)
        {
            HudDraw.Frame(r, 0.85f, new Color(edge.r, edge.g, edge.b, 0.8f));
            var tr = r;
            if (glyph != null)
            {
                HudDraw.Glyph(new Rect(r.x + 12f, r.y + 6f, r.height - 12f, r.height - 12f), glyph, edge);
            }
            HudDraw.Text(tr, text, HudStyles.BodyCenter, Ui.TextLight);
            // informational: no click blocking, units behind it stay clickable
        }

        // ================================================================ time & movement

        void DrawTurnRow(CombatController c, Unit u)
        {
            var row = HudLayout.TurnRow;
            HudDraw.Frame(row, 0.82f);
            Ui.Block(row);
            bool canAct = c.IsPlayerTurn;

            // --- Time
            float timeLeft = Mathf.Max(0f, u.TimeLeft);
            var gl = new Rect(row.x + 12f, row.y + 11f, 24f, 24f);
            HudDraw.Glyph(gl, "glyph_clock", Ui.Time);
            const float barW = 300f, gap = 4f;
            float segW = (barW - gap * 3f) / 4f;
            float bx = gl.xMax + 10f, by = row.y + 9f;
            HudDraw.Text(new Rect(bx, row.y - 1f, 200f, 16f), "TIME", HudStyles.Tiny, new Color(1f, 1f, 1f, 0.55f), false);
            by = row.y + 15f;

            // cost preview: the hovered bar slot, else the ability/item whose target is being chosen in the world
            var hov = Hud.HoveredSlot;
            float cost = 0f, castTime = 0f;
            bool channeled = false;
            if (canAct && hov != null && hov.Usable)
            {
                cost = hov.TimeCost;
                castTime = hov.CastTime;
                channeled = hov.Ability != null && hov.Ability.channeled;
            }
            else if (canAct && c.IsTargeting && c.TargetingAbility != null)
            {
                TargetingTime(u, c.TargetingAbility, c.TargetingItem != null ? 0 : c.TargetingRank, out cost, out castTime);
                channeled = c.TargetingAbility.channeled;
            }
            float from = timeLeft - cost;
            float pulse = HudDraw.Pulse(6f, 0.55f, 0.95f);
            for (int i = 0; i < 4; i++)
            {
                var sr = new Rect(bx + i * (segW + gap), by, segW, 20f);
                float s0 = i * Seg, s1 = (i + 1) * Seg;
                float fill = Mathf.Clamp01((timeLeft - s0) / Seg);
                HudDraw.Bar(sr, fill, canAct ? Ui.Time : new Color(Ui.Time.r, Ui.Time.g, Ui.Time.b, 0.55f));
                if (cost > 0.001f)
                {
                    float a0 = Mathf.Max(from, s0), a1 = Mathf.Min(timeLeft, s1);
                    if (a1 > a0) HudDraw.BarSegment(sr, (a0 - s0) / Seg, (a1 - s0) / Seg, new Color(1f, 0.85f, 0.4f, pulse));
                }
            }
            var barRect = new Rect(bx, by, barW, 20f);
            float tx = barRect.xMax + 10f;
            HudDraw.Text(new Rect(tx, by - 2f, 70f, 24f), timeText.Get(timeLeft), Ui.NumberStyle(19, TextAnchor.MiddleLeft), canAct ? Ui.TextLight : Hud.Muted);
            // preview/debt notes under the bar
            string note = null;
            Color noteCol = Ui.Bad;
            if (cost > 0.001f && cost > timeLeft + 0.001f)
            {
                if (castTime > 0.01f && channeled) { note = "channel continues into your next turn"; noteCol = Ui.Gold; }
                else if (castTime > 0.01f) { note = "becomes pending — resolves at your next turn"; noteCol = Ui.Gold; }
                else note = overflowText.Get(cost - timeLeft);
            }
            else if (u.TimeDebt > 0.01f) note = debtText.Get(-u.TimeDebt);
            if (note != null) HudDraw.Text(new Rect(bx + 120f, row.y - 1f, 260f, 16f), note, HudStyles.TinyRight, noteCol, false);
            if (HudDraw.Hover(barRect))
                Ui.TooltipFor(barRect, "<b>Time</b>: " + HudText.Secs(timeLeft) + " of 6.0 s left this turn.\n" +
                    Ui.Rich("Abilities cost Time (the GCD is 1.5 s). Instants that overflow become time debt for the next turn; casts that do not fit become pending and resolve at the start of your next turn.", Hud.Muted) +
                    (u.TimeDebt > 0.01f ? "\n" + Ui.Rich("Time debt: " + HudText.Secs(u.TimeDebt) + " s will be taken from your next turn.", Ui.Bad) : ""));

            // --- Movement
            float mx = tx + 78f;
            var mg = new Rect(mx, row.y + 11f, 24f, 24f);
            HudDraw.Glyph(mg, "glyph_boot", Hud.C("#d9c39a"));
            float mbx = mg.xMax + 8f;
            float mw = row.xMax - 12f - mbx;
            HudDraw.Text(new Rect(mbx, row.y - 1f, 200f, 16f), "MOVE", HudStyles.Tiny, new Color(1f, 1f, 1f, 0.55f), false);
            float budget = Mathf.Max(0.01f, u.MoveBudget);
            float left = Mathf.Max(0f, u.MoveLeft);
            var mr = new Rect(mbx, row.y + 17f, mw, 14f);
            bool rooted = false;
            try { rooted = u.IsRooted; } catch (Exception) { }
            HudDraw.Bar(mr, left / budget, rooted ? Hud.Muted : Hud.C("#c9b07a"));
            string mtxt = rooted ? "Rooted" : moveText.Get(left);
            HudDraw.Text(new Rect(mr.x, mr.y - 1f, mr.width * 0.5f, mr.height + 2f), mtxt, HudStyles.Tiny, rooted ? Ui.Bad : Ui.TextLight);
            HudDraw.Text(new Rect(mr.x, mr.y - 1f, mr.width - 4f, mr.height + 2f), budgetText.Get(u.MoveBudget), HudStyles.TinyRight, Hud.Muted);
            if (HudDraw.Hover(mr))
                Ui.TooltipFor(mr, "<b>Movement</b>: " + HudText.Secs(left) + " of " + HudText.Secs(u.MoveBudget) + " m left.\n" +
                                  Ui.Rich("Moving is free of Time. A unit with a pending cast cannot move; roots stop movement and snares shorten it.", Hud.Muted));
        }

        // Time cost / cast time of the ability (or item's use ability) being targeted: the same numbers the cursor
        // preview states (AbilityRules with the unit's mods). Cached per unit+ability, refreshed a few times a second
        // so haste/aura changes during a long targeting session still show.
        Unit tgtTimeUnit;
        AbilityDef tgtTimeAbility;
        int tgtTimeRank;
        float tgtTimeAt = -1f, tgtTimeCost, tgtCastTime;

        void TargetingTime(Unit u, AbilityDef a, int rank, out float cost, out float cast)
        {
            float now = Time.unscaledTime;
            if (u != tgtTimeUnit || a != tgtTimeAbility || rank != tgtTimeRank || now - tgtTimeAt > 0.25f || now < tgtTimeAt)
            {
                tgtTimeUnit = u;
                tgtTimeAbility = a;
                tgtTimeRank = rank;
                tgtTimeAt = now;
                tgtTimeCost = tgtCastTime = 0f;
                try
                {
                    AbilityModSet mods;
                    try { mods = AbilityMods.For(u, a); } catch (Exception) { mods = AbilityModSet.Empty; }
                    // a pinned lower rank may cast faster (rankCastTimes: Fireball Rank 1 is 1.5 s)
                    int used = AbilityRules.UsedRank(u, a, rank);
                    tgtTimeCost = AbilityRules.TimeCost(u, a, mods, used);
                    tgtCastTime = AbilityRules.CastTime(u, a, mods, used);
                }
                catch (Exception) { tgtTimeCost = tgtCastTime = 0f; }
            }
            cost = tgtTimeCost;
            cast = tgtCastTime;
        }

        // ================================================================ buttons

        void DrawButtons(CombatController c, Battle b, Unit turnUnit)
        {
            if (b.IsOver) return;
            var er = HudLayout.EndTurn;
            bool can = c.IsPlayerTurn && turnUnit != null;
            bool spent = can && turnUnit.TimeLeft <= 0.05f && (turnUnit.MoveLeft <= 0.1f || turnUnit.Pending != null);
            if (spent || (can && turnUnit.TimeLeft <= 0.05f))
                HudDraw.Glow(new Rect(er.x - 18f, er.y - 16f, er.width + 36f, er.height + 32f), new Color(1f, 0.85f, 0.45f, 0.5f * HudDraw.Pulse(4f, 0.4f, 1f)));
            if (can && endTipFor != turnUnit) { endTipFor = turnUnit; endTip = "End " + Hud.NameOf(turnUnit) + "'s turn. Auto attacks swing at the end of the turn."; }
            if (Ui.Btn(er, can ? "End Turn\n<size=13>Space</size>" : "End Turn", Ui.ButtonGold, can, can ? endTip : null))
                Hud.Post(() => Hud.Combat?.EndTurn());

            // fast-forward toggle
            var fr = new Rect(er.xMax + 8f, er.y, 40f, 30f);
            bool ff = c.FastForward;
            if (fr.xMax < Ui.Width - 4f)
            {
                if (Ui.Btn(fr, ff ? "<color=#ffd27a>»</color>" : "»", HudStyles.Button, true, ff ? "Fast animations: on (click to turn off)" : "Fast animations (or hold Shift)"))
                    Hud.Post(() =>
                    {
                        var cc = Hud.Combat;
                        if (cc == null) return;
                        cc.FastForward = !cc.FastForward;
                        if (InRaid) FastForwardRaidPref = cc.FastForward;
                        else FastForwardPref = cc.FastForward;
                    });
            }

            // leave a practice fight
            bool canLeave = false;
            try { canLeave = b.CanDisengage; } catch (Exception) { }
            if (canLeave)
            {
                var lr = new Rect(er.x, er.y - 42f, er.width, 34f);
                if (Ui.Btn(lr, "Leave Fight", HudStyles.Button, true, "Stop this practice fight (no experience or loot)."))
                    Hud.Post(() => Hud.Combat?.Disengage());
            }
        }

        // ================================================================ raid auto-play toggles

        // the flags each toggle changed, put back when it is turned off (per game; dropped when a raid begins or ends)
        AutoPlaySnapshot companionsSnap, battleSnap;
        GameSession snapSession;
        bool snapInRaid;
        readonly List<Unit> companionsTmp = new List<Unit>(12), everyoneTmp = new List<Unit>(12);

        const string CompanionsTipOn = "<b>Auto: all companions</b> (on)\nEvery companion's AI plays its turns (and its pet's). Click to give back the control you had before.";
        const string CompanionsTipOff = "<b>Auto: all companions</b>\nLet every companion's AI play its turns (and its pet's); you keep your own character. Click again to put each one back as it was.";
        const string BattleTipOn = "<b>Auto-battle</b> (on)\nThe AI fights for the whole party, your character too. Click to give back the control you had before.";
        const string BattleTipOff = "<b>Auto-battle</b>\nLet the AI fight for the whole party, your character too. Click again to put everyone back as they were.";

        /// <summary>The toggles show in raids and big battles (more than RaidPlanning.BigBattleUnits units).</summary>
        static bool ShowAutoToggles(Battle b) => b != null && !b.IsOver && (InRaid || RaidPlanning.IsBigBattle(b));

        void CollectParty(GameSession s)
        {
            companionsTmp.Clear();
            everyoneTmp.Clear();
            var party = s.Party;
            for (int i = 0; i < party.Count; i++)
            {
                var u = party[i];
                if (u == null) continue;
                everyoneTmp.Add(u);
                if (u != s.Main) companionsTmp.Add(u);
            }
        }

        static bool AllAuto(List<Unit> units)
        {
            if (units.Count == 0) return false;
            for (int i = 0; i < units.Count; i++) if (!units[i].AutoPlay) return false;
            return true;
        }

        void DrawAutoToggles(Battle b)
        {
            if (!ShowAutoToggles(b)) return;
            var s = Hud.Session;
            if (s == null || s.Main == null) return;
            CollectParty(s);
            bool companions = AllAuto(companionsTmp);
            bool everyone = AllAuto(everyoneTmp);
            // stacked above End Turn (and above Leave Fight in a practice fight)
            var er = HudLayout.EndTurn;
            bool leave = false;
            try { leave = b.CanDisengage; } catch (Exception) { }
            float top = er.y - (leave ? 42f : 0f) - 8f;
            // right-aligned with End Turn, 220 px wide (the label needs it); left of them only the turn row's end, lower down
            const float w = 220f;
            var battleR = new Rect(er.xMax - w, top - 30f, w, 30f);
            var compR = new Rect(er.xMax - w, battleR.y - 36f, w, 30f);
            if (companionsTmp.Count > 0 && AutoPill(compR, "Auto: all companions", companions, companions ? CompanionsTipOn : CompanionsTipOff))
                Hud.Post(() => ToggleCompanions(!companions));
            if (AutoPill(battleR, "Auto-battle", everyone, everyone ? BattleTipOn : BattleTipOff))
                Hud.Post(() => ToggleAutoBattle(!everyone));
        }

        /// <summary>A toggle pill (check box + label). True when clicked.</summary>
        static bool AutoPill(Rect r, string label, bool on, string tip)
        {
            bool hover = HudDraw.Hover(r);
            HudDraw.Frame(r, 0.85f, on ? new Color(0.5f, 0.83f, 0.48f, 0.95f) : hover ? new Color(1f, 0.85f, 0.45f, 0.8f) : (Color?)null);
            var box = new Rect(r.x + 8f, r.y + 7f, 16f, 16f);
            HudDraw.Fill(box, on ? new Color(0.2f, 0.45f, 0.22f, 0.95f) : new Color(0.04f, 0.03f, 0.08f, 0.85f), 3);
            HudDraw.Ring(box, on ? new Color(0.5f, 0.83f, 0.48f, 1f) : new Color(1f, 1f, 1f, 0.45f), 5);
            if (on) HudDraw.Fill(new Rect(box.x + 4f, box.y + 4f, box.width - 8f, box.height - 8f), new Color(0.75f, 1f, 0.7f, 1f), 3);
            HudDraw.Text(new Rect(box.xMax + 7f, r.y, r.width - (box.xMax + 7f - r.x) - 4f, r.height), label, HudStyles.NameSmall, on ? Color.white : Ui.TextLight);
            if (hover) Ui.TooltipFor(r, tip);
            Ui.Block(r);
            if (!HudDraw.Click(r)) return false;
            Ui.Sfx?.Invoke("ui_click");
            return true;
        }

        static void SetAuto(Unit u, bool on)
        {
            var c = Hud.Combat;
            if (c != null) c.SetAutoPlay(u, on);
            else Hud.Session?.SetAutoPlay(u, on);
        }

        void ToggleCompanions(bool on)
        {
            var s = Hud.Session;
            if (s == null) return;
            CollectParty(s);
            if (on)
            {
                companionsSnap = AutoPlaySnapshot.Capture(companionsTmp);
                for (int i = 0; i < companionsTmp.Count; i++) SetAuto(companionsTmp[i], true);
                return;
            }
            if (companionsSnap != null) companionsSnap.Restore(SetAuto, companionsTmp, false);
            else for (int i = 0; i < companionsTmp.Count; i++) SetAuto(companionsTmp[i], false);
            companionsSnap = null;
        }

        void ToggleAutoBattle(bool on)
        {
            var s = Hud.Session;
            if (s == null) return;
            CollectParty(s);
            if (on)
            {
                battleSnap = AutoPlaySnapshot.Capture(everyoneTmp);
                for (int i = 0; i < everyoneTmp.Count; i++) SetAuto(everyoneTmp[i], true);
                return;
            }
            if (battleSnap != null) battleSnap.Restore(SetAuto, everyoneTmp, false);
            else if (s.Main != null) SetAuto(s.Main, false);   // no record of before: hand the main character back
            battleSnap = null;
        }

        // ================================================================ cursor hint

        void DrawCursorPreview(CombatController c)
        {
            if (!c.IsPlayerTurn || GameInput.PointerOverUi) return;
            string text = c.HoverPreview;
            if (string.IsNullOrEmpty(text)) return;
            // The preview carries the time/cost/pending part at its end ("… · 3.5 s cast (pending: resolves next
            // turn, can be interrupted)"), so it wraps instead of being cut off: one line up to PreviewMaxText px,
            // then as many lines as it needs. Measured once per preview string (and when the styles are rebuilt).
            var st = HudStyles.SmallWrap ?? HudStyles.Small;
            if (st == null) return;
            if (!ReferenceEquals(text, measuredText) || measuredStyles != HudStyles.Version)
            {
                measure.text = text;
                bool wrap = st.wordWrap;
                float tw;
                st.wordWrap = false;
                try { tw = st.CalcSize(measure).x + 2f; }   // +2: no wrap from rounding on single-line previews
                finally { st.wordWrap = wrap; }
                tw = Mathf.Min(tw, PreviewMaxText);
                float th = wrap ? st.CalcHeight(measure, tw) : st.CalcSize(measure).y;
                measuredSize = new Vector2(Mathf.Ceil(tw), Mathf.Ceil(th));
                measuredText = text;
                measuredStyles = HudStyles.Version;
            }
            var m = HudDraw.Mouse;
            float w = measuredSize.x + PreviewPadX * 2f, h = Mathf.Max(26f, measuredSize.y + PreviewPadY * 2f);
            float x = m.x + 22f, y = m.y + 22f;
            if (x + w > Ui.Width - 6f) x = m.x - w - 12f;
            if (y + h > Ui.Height - 6f) y = m.y - h - 12f;
            x = Mathf.Clamp(x, 6f, Mathf.Max(6f, Ui.Width - 6f - w));
            y = Mathf.Clamp(y, 6f, Mathf.Max(6f, Ui.Height - 6f - h));
            var r = new Rect(x, y, w, h);
            HudDraw.Fill(r, new Color(0.08f, 0.06f, 0.14f, 0.86f), 8);
            HudDraw.Ring(r, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.5f), 8);
            HudDraw.Text(new Rect(r.x + PreviewPadX, r.y + (h - measuredSize.y) * 0.5f, measuredSize.x, measuredSize.y), text, st, Ui.TextLight);
        }
    }

    /// <summary>Soulstone / Reincarnation offer for the active downed unit.</summary>
    public sealed class SelfResPromptHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderSelfRes;
        public bool Visible
        {
            get
            {
                if (!Hud.CombatHud) return false;
                var c = Hud.Combat;
                try { return c != null && c.PendingSelfResurrection != null; }
                catch (Exception) { return false; }
            }
        }
        public bool Modal => false;

        public void Tick(float dt) { }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try
            {
                HudStyles.Ensure();
                var c = Hud.Combat;
                var offer = c != null ? c.PendingSelfResurrection : null;
                var u = c != null ? c.ActiveUnit : null;
                if (offer == null || u == null) return;
                float w = 540f, h = 178f;
                var r = new Rect(Mathf.Round(Ui.Width * 0.5f - w * 0.5f), Mathf.Round(Ui.Height * 0.36f), w, h);
                HudDraw.Glow(new Rect(r.x - 60f, r.y - 50f, r.width + 120f, r.height + 100f), new Color(0.6f, 0.4f, 1f, 0.25f));
                Ui.Panel(r, Ui.InkPanel, true);
                string title = string.IsNullOrEmpty(offer.Name) ? "Resurrection" : offer.Name;
                HudDraw.Text(new Rect(r.x, r.y + 14f, r.width, 40f), title, HudStyles.TitleBig, Ui.Gold);
                string body = Hud.NameOf(u) + " can rise again now with " + Mathf.RoundToInt(offer.Health) + " health" +
                              (offer.Mana > 0f ? " and " + Mathf.RoundToInt(offer.Mana) + " mana" : "") + ".";
                HudDraw.Text(new Rect(r.x + 20f, r.y + 62f, r.width - 40f, 44f), body, HudStyles.BodyCenter, Ui.TextLight);
                var a = new Rect(r.x + w * 0.5f - 190f, r.yMax - 58f, 180f, 42f);
                var d = new Rect(r.x + w * 0.5f + 10f, r.yMax - 58f, 180f, 42f);
                if (Ui.Btn(a, "Rise", Ui.ButtonGold)) Hud.Post(() => Hud.Combat?.AnswerSelfResurrection(true));
                if (Ui.Btn(d, "Stay down", HudStyles.Button, true, "Wait for an ally to help you up or resurrect you.")) Hud.Post(() => Hud.Combat?.AnswerSelfResurrection(false));
            }
            catch (Exception e) { Hud.LogOnce("selfres:" + e.GetType().Name, "Self-res prompt: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }
    }
}
