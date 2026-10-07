// Raid frames (left column, in a raid: GameSession.InRaid — the party frames step aside). Compact WoW-style frames in two
// groups of five (column-major: members 1–5 left, 6–10 right): class-colour bar, name in class colour, leader crown,
// health with absorb, a thin resource bar, up to four debuffs (dispel-type frames), the AUTO pill (companions), the gold
// ring of the acting unit, a red ring + attacker count for aggro, pending-cast and dead/downed states; pets and controlled
// summons as thin rows under their owner. The click contract is the party frames': a click selects (Hud.ClickUnit), confirms
// a combat target (Hud.TargetStateOf drives the look) or answers the out-of-combat "choose a party member" pick
// (Hud.FieldPick / IsValidPickTarget / ResolveFieldPick). In combat health and death are read as presented (HudPresented).
// The groups never reach the bottom HUD block (compact log, action bar): pet rows give way first.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class RaidFramesHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderPartyFrames + 1;
        public bool Visible
        {
            get
            {
                if (!Hud.WorldHud) return false;
                var s = Hud.Session;
                return s != null && s.InRaid;
            }
        }
        public bool Modal => false;

        /// <summary>Members per group (one column each).</summary>
        public const int GroupSize = 5;
        // two columns of 145 + 6 (right edge 310, inside the party frames' 314): the turn strip keeps its room at every
        // interface size (TurnOrderHud sizes itself to leave QuestTrackerHud.W + 26 px either side)
        const float X = 14f, Y = 14f, CW = 145f, ColGap = 6f, FH = 64f, RowGap = 5f;
        const float PetH = 15f, PetIndent = 12f, PetGap = 2f;
        const float DebuffSize = 16f, DebuffGap = 3f;
        const int MaxDebuffs = 4, MaxSubs = 2;

        static readonly Color FrameDead = new Color(0.55f, 0.52f, 0.6f, 1f);
        static readonly Color AutoOn = Ui.Hex("#7fd47a");
        static readonly Color AggroCol = new Color(1f, 0.3f, 0.25f, 1f);

        readonly List<Unit> chars = new List<Unit>(12);
        readonly List<Unit> subs = new List<Unit>(4);
        readonly List<AuraInstance> debuffs = new List<AuraInstance>(8);

        // tooltip text cache (built only while hovered)
        Unit tipUnit;
        int tipFrame;
        string tipText = "";

        public void Tick(float dt) { }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try
            {
                HudStyles.Ensure();
                var s = Hud.Session;
                if (s == null) return;
                chars.Clear();
                var party = s.Party;
                for (int i = 0; i < party.Count; i++) if (party[i] != null) chars.Add(party[i]);
                if (chars.Count == 0) return;

                var combat = Hud.Combat;
                var battle = combat != null ? combat.Battle : null;
                float bottom = HudLayout.CompactLog.y - 8f;
                int perCol = Mathf.Max(GroupSize, (chars.Count + 1) / 2);
                bool pets = ColumnsFit(perCol, battle, bottom, true);
                for (int col = 0; col * perCol < chars.Count; col++)
                {
                    float x = X + col * (CW + ColGap);
                    float y = Y;
                    int end = Mathf.Min(chars.Count, (col + 1) * perCol);
                    for (int i = col * perCol; i < end; i++)
                    {
                        if (i > col * perCol) y += RowGap;
                        y = DrawMember(chars[i], x, y, s, battle);
                        if (pets) y = DrawSubs(chars[i], x, y, battle);
                    }
                }
            }
            catch (Exception e) { Hud.LogOnce("raidframes:" + e.GetType().Name, "Raid frames: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        /// <summary>True when every column, with its pet rows (when pets is set), ends above bottom.</summary>
        bool ColumnsFit(int perCol, Battle battle, float bottom, bool pets)
        {
            for (int col = 0; col * perCol < chars.Count; col++)
            {
                float h = 0f;
                int end = Mathf.Min(chars.Count, (col + 1) * perCol);
                for (int i = col * perCol; i < end; i++)
                {
                    if (i > col * perCol) h += RowGap;
                    h += FH;
                    if (pets)
                    {
                        CollectSubs(chars[i], battle);
                        h += subs.Count * (PetGap + PetH);
                    }
                }
                if (Y + h > bottom + 0.5f) return false;
            }
            return true;
        }

        // ================================================================ member frame

        float DrawMember(Unit u, float x, float y, GameSession s, Battle battle)
        {
            var r = new Rect(x, y, CW, FH);
            bool active = battle != null && !battle.IsOver && HudPresented.ActiveUnit(battle) == u;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == u;
            var pick = Hud.FieldPick;
            bool pickable = pick != null && Hud.IsValidPickTarget(u);
            var classCol = Hud.UnitColor(u);
            bool dead = HudPresented.Dead(u), downed = !dead && HudPresented.Downed(u);
            var tstate = Hud.TargetStateOf(u);

            // backdrop & highlights
            if (active)
            {
                float p = HudDraw.Pulse(4f, 0.35f, 0.7f);
                HudDraw.Glow(new Rect(r.x - 18f, r.y - 14f, r.width + 36f, r.height + 28f), new Color(1f, 0.82f, 0.4f, 0.3f * p));
            }
            HudDraw.Frame(r, 0.8f, active ? new Color(1f, 0.85f, 0.45f, 0.95f) : (Color?)null);
            // class bar
            HudDraw.Fill(new Rect(r.x + 3f, r.y + 4f, 5f, r.height - 8f), dead || downed ? FrameDead : classCol, 3);

            // name (+ leader crown) and the AUTO pill
            float cx = r.x + 12f, cw = r.xMax - cx - 6f;
            bool companion = u.Companion != null || u.Kind == UnitKind.Companion;
            // the pill: every companion; the main character too while the AI plays it (Auto-battle), to take it back
            bool pill = companion || u.AutoPlay;
            float nameX = cx;
            if (s.Leader == u && s.Party.Count > 1)
            {
                var cr = new Rect(cx - 1f, r.y + 4f, 15f, 15f);
                HudDraw.Glyph(cr, "glyph_crown", Ui.Gold);
                if (HudDraw.Hover(cr)) Ui.TooltipFor(cr, "Raid leader — the camera follows them and they lead the walk.");
                nameX += 16f;
            }
            float nameW = r.xMax - 6f - nameX - (pill ? 42f : 0f);
            HudDraw.Text(new Rect(nameX, r.y + 2f, nameW, 20f), Hud.NameOf(u), HudStyles.NameSmall, dead ? Hud.Muted : classCol);
            bool overToggle = false;
            if (pill)
            {
                var ar = new Rect(r.xMax - 45f, r.y + 4f, 40f, 16f);
                bool on = u.AutoPlay;
                overToggle = DrawAutoToggle(ar, u, on, true,
                    HudDraw.Hover(ar)
                        ? (on ? "<b>Auto-play on</b>\nThe AI plays " + Hud.NameOf(u) + "'s turns in combat (and the pet's). Click to take control."
                              : "<b>Auto-play off</b>\nYou control this companion in combat. Click to let its AI play.")
                        : null);
            }

            // health (+ absorb), with the dead/downed word on it
            var labels = HudText.For(u);
            var hr = new Rect(cx, r.y + 22f, cw, 16f);
            PartyFramesHud.DrawHealth(hr, u, dead || downed ? null : labels.Health, HudStyles.TinyCenter);
            if (dead) HudDraw.Text(new Rect(hr.x, hr.y - 1f, hr.width, hr.height + 2f), "Dead", HudStyles.TinyCenter, Ui.Bad);
            else if (downed) HudDraw.Text(new Rect(hr.x, hr.y - 1f, hr.width, hr.height + 2f), "Downed", HudStyles.TinyCenter, Ui.Bad);

            // resource (thin) or the pending cast
            var rr = new Rect(cx, r.y + 40f, cw, 5f);
            if (u.Pending != null && u.Pending.Ability != null) PartyFramesHud.DrawCastBar(new Rect(cx, r.y + 39f, cw, 7f), u.Pending, true);
            else
            {
                var res = u.PowerType;
                float maxRes = res != ResourceType.None ? u.MaxResource(res) : 0f;
                if (maxRes > 0f) HudDraw.Bar(rr, u.GetResource(res) / maxRes, Ui.ResourceColor(res));
            }

            // debuffs (what a dispel or a heal answers), up to four
            DrawDebuffs(u, cx, r.y + 46f);

            // aggro: enemies attacking this member
            if (battle != null && !battle.IsOver && !dead)
            {
                int attackers = AttackersOf(u, battle);
                if (attackers > 0)
                {
                    HudDraw.Ring(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(AggroCol.r, AggroCol.g, AggroCol.b, HudDraw.Pulse(3f, 0.35f, 0.75f)), 8);
                    var ab = new Rect(r.xMax - 33f, r.yMax - 19f, 29f, 16f);
                    HudDraw.Fill(ab, new Color(0.55f, 0.08f, 0.08f, 0.92f), 5);
                    HudDraw.Glyph(new Rect(ab.x + 2f, ab.y + 2f, 12f, 12f), "glyph_swords", Color.white);
                    HudDraw.Text(new Rect(ab.x + 14f, ab.y - 2f, 14f, 20f), HudText.Int(attackers), HudStyles.TinyCenter, Color.white, false);
                    if (HudDraw.Hover(ab)) Ui.TooltipFor(ab, attackers == 1 ? "An enemy is attacking " + Hud.NameOf(u) + "." : attackers + " enemies are attacking " + Hud.NameOf(u) + ".");
                }
            }

            // rings: acting unit, selection, pick / target states (drawn last, over the bars)
            if (active) HudDraw.Ring(r, new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)), 8, true);
            else if (selected) HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.6f), 8);
            if (pick != null)
            {
                if (pickable) HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.45f, 1f)), 8, true);
                else HudDraw.Fill(r, new Color(0f, 0f, 0f, 0.35f), 8);
            }
            DrawTargetState(r, tstate);
            if (dead) HudDraw.Fill(r, new Color(0f, 0f, 0f, 0.25f), 8);

            // click → select / pick / target; tooltip
            if (HudDraw.Hover(r) && !overToggle && !overDebuff) Ui.TooltipFor(r, TipFor(u, tstate));
            Ui.Block(r);
            if (!overToggle && HudDraw.Click(r))
            {
                var unit = u;
                if (pick != null) Hud.ResolveFieldPick(unit);
                else Hud.ClickUnit(unit);
                Ui.Sfx?.Invoke("ui_click");
            }
            return r.yMax;
        }

        bool overDebuff;

        void DrawDebuffs(Unit u, float x, float y)
        {
            overDebuff = false;
            debuffs.Clear();
            var list = u.Auras;
            for (int i = 0; i < list.Count && debuffs.Count < MaxDebuffs; i++)
            {
                var a = list[i];
                if (a == null || a.Def == null || a.Def.hidden || a.IsPassive || !a.IsDebuff) continue;
                debuffs.Add(a);
            }
            for (int i = 0; i < debuffs.Count; i++)
            {
                var a = debuffs[i];
                var ar = new Rect(x + i * (DebuffSize + DebuffGap), y, DebuffSize, DebuffSize);
                HudDraw.Icon(ar, string.IsNullOrEmpty(a.Def.icon) ? "glyph_aura" : a.Def.icon, PartyFramesHud.DebuffColor(a.Def.dispel));
                if (a.Stacks > 1) HudDraw.Text(new Rect(ar.x, ar.y - 5f, ar.width + 2f, 14f), HudText.Int(a.Stacks), HudStyles.TinyRight, Ui.TextLight);
                if (HudDraw.Hover(ar))
                {
                    overDebuff = true;
                    string tip = UiText.Aura(a);
                    if (a.Duration > 0f && a.Remaining > 0f && !a.IsPermanent) tip += "\n" + Ui.Rich(HudText.Secs(a.Remaining) + " s left", Hud.Muted);
                    if (a.Caster != null && a.Caster != a.Bearer) tip += "\n" + Ui.Rich("From " + Hud.NameOf(a.Caster), Hud.Muted);
                    Ui.TooltipFor(ar, tip);
                }
            }
        }

        // ================================================================ pets & controlled summons

        void CollectSubs(Unit owner, Battle battle)
        {
            subs.Clear();
            if (battle != null)
            {
                var units = battle.Units;
                for (int i = 0; i < units.Count; i++)
                {
                    var p = units[i];
                    if (p == null || p.Owner != owner || p.IsTotem || HudPresented.Dead(p)) continue;
                    if (p.Team != owner.Team && !p.OriginalTeam.HasValue) continue;
                    if (subs.Count < MaxSubs) subs.Add(p);
                }
                return;
            }
            if (owner.Pet != null && !owner.Pet.Dead) subs.Add(owner.Pet);
            for (int i = 0; i < owner.Summons.Count && subs.Count < MaxSubs; i++)
            {
                var p = owner.Summons[i];
                if (p != null && p != owner.Pet && !p.Dead && !p.IsTotem) subs.Add(p);
            }
        }

        float DrawSubs(Unit owner, float x, float y, Battle battle)
        {
            CollectSubs(owner, battle);
            for (int i = 0; i < subs.Count; i++) y = DrawPet(subs[i], x + PetIndent, y + PetGap, battle);
            return y;
        }

        float DrawPet(Unit p, float x, float y, Battle battle)
        {
            var r = new Rect(x, y, CW - PetIndent, PetH);
            bool active = battle != null && !battle.IsOver && HudPresented.ActiveUnit(battle) == p;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == p;
            var pick = Hud.FieldPick;
            var tstate = Hud.TargetStateOf(p);
            PartyFramesHud.DrawHealth(r, p, null, null);
            string name = Hud.NameOf(p);
            HudDraw.Text(new Rect(r.x + 5f, r.y - 2f, r.width - 10f, r.height + 4f), name, HudStyles.Tiny, Hud.UnitColor(p));
            if (active) HudDraw.Ring(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)), 5);
            else if (selected) HudDraw.Ring(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(1f, 1f, 1f, 0.55f), 5);
            if (pick != null && Hud.IsValidPickTarget(p)) HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.45f, 1f)), 5);
            switch (tstate)
            {
                case Hud.TargetState.Valid: HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.5f, 1f)), 5); break;
                case Hud.TargetState.Reachable: HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.2f, 0.5f)), 5); break;
                case Hud.TargetState.Invalid: HudDraw.Fill(r, new Color(0f, 0f, 0f, 0.35f), 3); break;
            }
            if (HudDraw.Hover(r))
            {
                string tip = "<b>" + name + (p.OriginalTeam.HasValue ? " (controlled)" : "") + "</b>\nHealth " +
                             Mathf.CeilToInt(HudPresented.Health(p)) + " / " + Mathf.RoundToInt(p.MaxHealth) +
                             (p.Lifetime > 0f ? "\n" + Ui.Rich(UiText.Duration(p.Lifetime) + " remaining", Hud.Muted) : "") + "\n" +
                             (tstate != Hud.TargetState.None ? TargetHint(p, tstate)
                              : Ui.Rich(pick != null ? "Click to choose." : "Click to select (its abilities appear on the action bar).", Hud.Muted));
                Ui.TooltipFor(r, tip);
            }
            Ui.Block(r);
            if (HudDraw.Click(r))
            {
                var unit = p;
                if (pick != null) Hud.ResolveFieldPick(unit);
                else Hud.ClickUnit(unit);
                Ui.Sfx?.Invoke("ui_click");
            }
            return r.yMax;
        }

        // ================================================================ shared bits (as the party frames)

        static int AttackersOf(Unit u, Battle b)
        {
            int n = 0;
            var units = b.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var e = units[i];
                if (e == null || !e.IsAlive || e.Team == b.PlayerTeam || e.IsTotem) continue;
                var t = e.TauntedBy ?? e.AggroTarget;
                if (t == u) n++;
            }
            return n;
        }

        /// <summary>The AUTO pill. Returns true while the mouse is over it (the frame click is skipped).</summary>
        static bool DrawAutoToggle(Rect ar, Unit u, bool on, bool enabled, string tip)
        {
            HudDraw.Fill(ar, on ? new Color(0.2f, 0.45f, 0.22f, enabled ? 0.95f : 0.6f) : new Color(0.16f, 0.13f, 0.24f, 0.9f), 5);
            HudDraw.Ring(ar, on ? new Color(AutoOn.r, AutoOn.g, AutoOn.b, enabled ? 1f : 0.45f) : new Color(1f, 1f, 1f, 0.3f), 5);
            HudDraw.Text(new Rect(ar.x, ar.y - 1f, ar.width, ar.height + 2f), "AUTO", HudStyles.TinyCenter, on ? Color.white : Hud.Muted, false);
            bool over = HudDraw.Hover(ar);
            if (over && tip != null) Ui.TooltipFor(ar, tip);
            Ui.Block(ar);
            if (enabled && HudDraw.Click(ar))
            {
                var unit = u;
                bool want = !on;
                Hud.Post(() =>
                {
                    var c = Hud.Combat;
                    if (c != null) c.SetAutoPlay(unit, want);
                    else Hud.Session?.SetAutoPlay(unit, want);
                });
                Ui.Sfx?.Invoke("ui_click");
            }
            return over;
        }

        static void DrawTargetState(Rect r, Hud.TargetState st)
        {
            switch (st)
            {
                case Hud.TargetState.Valid:
                    HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.5f, 1f)), 8, true);
                    break;
                case Hud.TargetState.Reachable:
                    HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.2f, 0.5f)), 8);
                    break;
                case Hud.TargetState.Invalid:
                    HudDraw.Fill(r, new Color(0f, 0f, 0f, 0.35f), 8);
                    break;
            }
        }

        static string TargetHint(Unit u, Hud.TargetState st)
        {
            var c = Hud.Combat;
            string what = c != null && c.TargetingItem != null ? c.TargetingItem.Name : c != null && c.TargetingAbility != null ? c.TargetingAbility.name : "it";
            switch (st)
            {
                case Hud.TargetState.Valid: return Ui.Rich("Click to use " + what + " on " + Hud.NameOf(u) + ".", Ui.Good);
                case Hud.TargetState.Reachable: return Ui.Rich("Out of range — a click walks into range first, then uses " + what + ".", Ui.Gold);
                default: return Ui.Rich(what + " cannot be used on " + Hud.NameOf(u) + ".", Ui.Bad);
            }
        }

        string TipFor(Unit u, Hud.TargetState tst)
        {
            if (tst != Hud.TargetState.None)
            {
                var cls0 = u.Class != null ? u.Class.name : "";
                return "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + " " + cls0 + "\n" + TargetHint(u, tst);
            }
            if (tipUnit == u && tipFrame > Time.frameCount - 15) return tipText;
            tipUnit = u;
            tipFrame = Time.frameCount;
            var cls = u.Class != null ? u.Class.name : "";
            string role = "";
            try { role = u.IsCharacter ? UiText.Spaced(u.Role.ToString()) : ""; } catch (Exception) { }
            var res = u.PowerType;
            float maxRes = res != ResourceType.None ? u.MaxResource(res) : 0f;
            var flow = Hud.Flow;
            string click = Hud.FieldPick != null ? "Click to choose this party member."
                         : flow != null && flow.Selected == u && flow.CanTalkTo(u) ? "Selected. Click " + Hud.NameOf(u) + " in the world to talk."
                         : "Click to select.";
            tipText = "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + " " + cls + (role.Length > 0 ? "  ·  " + role : "") +
                      "\nHealth " + Mathf.CeilToInt(HudPresented.Health(u)) + " / " + Mathf.RoundToInt(u.MaxHealth) +
                      (maxRes > 0f ? "\n" + HudText.ResourceName(res) + " " + Mathf.FloorToInt(u.GetResource(res)) + " / " + Mathf.RoundToInt(maxRes) : "") +
                      (u.Pending != null && u.Pending.Ability != null ? "\n" + Ui.Rich("Casting " + u.Pending.Ability.name, Ui.Gold) : "") +
                      "\n" + Ui.Rich(click, Hud.Muted);
            return tipText;
        }
    }
}
