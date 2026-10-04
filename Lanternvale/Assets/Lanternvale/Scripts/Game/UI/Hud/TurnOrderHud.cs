// Turn order (top centre): initiative portraits for Battle.TurnOrder starting at the acting unit (enlarged,
// gold ring), team-coloured frames, small health bars, a divider where the next round begins, the round
// number, and a hover tooltip (name, level, rank, health). Clicking a party portrait selects it.
// The strip follows the PRESENTED turn and deaths (HudPresented): it moves on when the next turn is shown on screen.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class TurnOrderHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderTurnOrder;
        public bool Visible => Hud.CombatHud;
        public bool Modal => false;

        public const float Top = 10f, Big = 70f, Small = 50f, Gap = 6f, DividerW = 16f;
        /// <summary>Bottom of the strip (incl. the active unit's name) — the target frame sits below it.</summary>
        public const float Bottom = Top + Big + 24f;

        readonly List<Unit> display = new List<Unit>(20);
        readonly List<bool> nextRound = new List<bool>(20);
        readonly HudText.One roundText = new HudText.One("Round ");
        readonly HudText.One nextRoundText = new HudText.One("R");
        Unit tipUnit;
        int tipStamp = -1;
        string tip = "";

        public void Tick(float dt) { }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("order:" + e.GetType().Name, "Turn order: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        void DrawInner()
        {
            HudStyles.Ensure();
            var b = Hud.Battle;
            if (b == null) return;
            var order = b.TurnOrder;
            int n = order.Count;
            if (n == 0) return;

            var shownActive = HudPresented.ActiveUnit(b);
            int start = shownActive != null ? order.IndexOf(shownActive) : -1;
            if (start < 0) start = Mathf.Clamp(b.TurnIndex, 0, n - 1);
            // leave room for the clock/gold pill (top right) and the party frames (top left)
            int maxShown = Mathf.Clamp((int)((Ui.Width - 2f * (QuestTrackerHud.W + HudLayout.Margin + 12f) - 140f) / (Small + Gap)), 4, 18);

            display.Clear();
            nextRound.Clear();
            bool wrappedMarked = false;
            for (int k = 0; k < n && display.Count < maxShown; k++)
            {
                int i = (start + k) % n;
                var u = order[i];
                if (u == null || u.IsTotem || HudPresented.Dead(u)) continue;
                bool wrapped = start + k >= n;
                display.Add(u);
                nextRound.Add(wrapped && !wrappedMarked);
                if (wrapped) wrappedMarked = true;
            }
            if (display.Count == 0) return;

            // total width
            float total = 0f;
            for (int i = 0; i < display.Count; i++)
            {
                total += i == 0 ? Big : Small;
                if (i > 0) total += Gap;
                if (nextRound[i]) total += DividerW;
            }
            float x = Mathf.Round(Ui.Width * 0.5f - total * 0.5f);
            var back = new Rect(x - 104f, Top - 6f, total + 116f, Big + 12f);
            HudDraw.Frame(back, 0.55f);
            Ui.Block(back);

            // round label
            var rl = new Rect(back.x + 8f, Top + Big * 0.5f - 20f, 88f, 22f);
            HudDraw.Text(rl, roundText.Get(Mathf.Max(1, b.Round)), HudStyles.Header, Ui.Gold);
            HudDraw.Text(new Rect(rl.x, rl.yMax - 1f, 88f, 18f), "initiative", HudStyles.Tiny, Hud.Muted, false);

            var active = shownActive;
            int hovered = -1;
            Rect hoveredRect = default;
            for (int i = 0; i < display.Count; i++)
            {
                if (nextRound[i])
                {
                    var dr = new Rect(x + 2f, Top + 6f, DividerW - 4f, Big - 12f);
                    HudDraw.Solid(new Rect(dr.center.x - 1f, dr.y, 2f, dr.height - 14f), new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.6f));
                    HudDraw.Text(new Rect(dr.x - 8f, dr.yMax - 16f, dr.width + 16f, 18f), nextRoundText.Get(b.Round + 1), HudStyles.TinyCenter, Ui.Gold, false);
                    x += DividerW;
                }
                var u = display[i];
                bool isActive = u == active;
                float size = i == 0 ? Big : Small;
                var r = new Rect(x, Top + (Big - size) * 0.5f, size, size);
                DrawEntry(r, u, isActive, b);
                if (HudDraw.Hover(r)) { hovered = i; hoveredRect = r; }
                if (HudDraw.Click(r))
                {
                    var unit = u;
                    var cc = Hud.Combat;
                    if (cc != null && cc.IsTargeting) Hud.ClickUnit(unit);          // pick the target from the strip
                    else if (u.Team == b.PlayerTeam) Hud.Post(() => Hud.Flow?.Select(unit));
                }
                x += size + Gap;
            }

            // active unit name under the strip
            if (active != null)
            {
                var first = new Rect(Mathf.Round(Ui.Width * 0.5f - total * 0.5f), Top, Big, Big);
                var nr = new Rect(first.center.x - 120f, back.yMax + 1f, 240f, 20f);
                HudDraw.Text(nr, Hud.NameOf(active), HudStyles.NameSmall, active.Team == b.PlayerTeam ? Hud.UnitColor(active) : Hud.EnemyTeam);
            }

            if (hovered >= 0) Ui.TooltipFor(hoveredRect, TipFor(display[hovered], b));
        }

        void DrawEntry(Rect r, Unit u, bool isActive, Battle b)
        {
            bool party = u.Team == b.PlayerTeam;
            var team = party ? Hud.PartyTeam : Hud.EnemyTeam;
            if (isActive) HudDraw.Glow(new Rect(r.x - 16f, r.y - 14f, r.width + 32f, r.height + 28f), new Color(1f, 0.82f, 0.42f, 0.5f * HudDraw.Pulse(4f, 0.5f, 1f)));
            bool downed = HudPresented.Downed(u);
            bool dim = downed || u.IsControlled || (u.Surprised && b.Round <= 1);
            HudDraw.Portrait(r, u, isActive ? new Color(1f, 0.86f, 0.45f, 1f) : team, dim);
            // team stripe
            HudDraw.Solid(new Rect(r.x + 6f, r.y + 2f, r.width - 12f, 3f), new Color(team.r, team.g, team.b, 0.9f));
            // health
            float pct = u.MaxHealth > 0f ? Mathf.Clamp01(HudPresented.Health(u) / u.MaxHealth) : 0f;
            var hb = new Rect(r.x + 4f, r.yMax - 8f, r.width - 8f, 5f);
            HudDraw.Solid(hb, new Color(0f, 0f, 0f, 0.7f));
            HudDraw.Solid(new Rect(hb.x, hb.y, hb.width * pct, hb.height), Hud.HealthColor(pct));
            if (u.Pending != null) HudDraw.Glyph(new Rect(r.xMax - 18f, r.y + 4f, 15f, 15f), "glyph_hourglass", Hud.SchoolCol(u.Pending.Ability != null ? u.Pending.Ability.school : School.Arcane));
            if (downed) HudDraw.Text(new Rect(r.x, r.y + r.height * 0.3f - 1f, r.width, 18f), "Down", HudStyles.TinyCenter, Ui.Bad);
            var rank = u.Rank;
            if (!party && (rank == CreatureRank.Elite || rank == CreatureRank.Rare || rank == CreatureRank.Boss))
                HudDraw.Glyph(new Rect(r.x + 2f, r.y + 3f, 14f, 14f), rank == CreatureRank.Boss ? "glyph_skull" : "glyph_star", rank == CreatureRank.Rare ? Hud.C("#d9e1f2") : Ui.Gold);
        }

        string TipFor(Unit u, Battle b)
        {
            int stamp = Time.frameCount / 10;
            if (u == tipUnit && stamp == tipStamp) return tip;
            tipUnit = u;
            tipStamp = stamp;
            string rank = Hud.RankName(u.Rank);
            string kind = u.Class != null ? u.Class.name : (u.Creature != null ? UiText.Spaced(u.CreatureType.ToString()) : "");
            tip = "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + (rank.Length > 0 ? " " + rank : "") + (kind.Length > 0 ? " " + kind : "") +
                  "\nHealth " + Mathf.CeilToInt(HudPresented.Health(u)) + " / " + Mathf.RoundToInt(u.MaxHealth) +
                  " (" + Mathf.RoundToInt(u.MaxHealth > 0 ? HudPresented.Health(u) / u.MaxHealth * 100f : 0f) + "%)";
            if (u.Pending != null && u.Pending.Ability != null) tip += "\n" + Ui.Rich("Casting " + u.Pending.Ability.name + " (" + HudText.Secs(u.Pending.RemainingTime) + " s left)", Ui.Gold);
            if (u.Surprised && b.Round <= 1) tip += "\n" + Ui.Rich("Surprised — loses its first turn", Ui.Bad);
            if (HudPresented.Downed(u)) tip += "\n" + Ui.Rich("Downed", Ui.Bad);
            return tip;
        }
    }
}
