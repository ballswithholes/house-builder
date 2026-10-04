// Target frame (top centre, combat): the hovered unit, else the acting enemy, else the active unit's attack/combo
// target (sticky). Name, level (skull for bosses and far higher levels), rank with an elite "dragon" frame, health
// with absorbs, resource, a telegraphed cast bar for pending casts with an interrupt prompt when the active player
// unit has a usable interrupt, buffs/debuffs (your own highlighted), combo points, and threat: who it attacks and
// your share of the threat.
// The frame is informational and does not block world clicks: it is often shown BECAUSE a unit under it is hovered,
// and a blocker would drop that hover on the next frame (flicker, lost clicks). Health/death are read as presented.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class TargetFrameHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderTargetFrame;
        public bool Visible => Hud.CombatHud;
        public bool Modal => false;

        const float W = 476f, Portrait = 76f, AuraSize = 26f;

        Unit sticky;
        readonly List<AuraInstance> auras = new List<AuraInstance>(24);
        readonly HudText.Pair health = new HudText.Pair();
        readonly HudText.Pair resource = new HudText.Pair();
        readonly HudText.One pctText = new HudText.One("", "%");
        readonly HudText.One level = new HudText.One("");
        readonly HudText.One threatPct = new HudText.One("Your threat ", "%");
        readonly HudText.Tenths castLeft = new HudText.Tenths("", " s");
        string attackLine = "";
        Unit attackFor, attackOf;
        string interruptLine = "";
        string interruptFor = "", interruptKey = "";

        public void Tick(float dt)
        {
            var b = Hud.Battle;
            if (b == null) { sticky = null; Hud.FramedTarget = null; }
        }

        public void Draw()
        {
            var layer = HudDraw.BeginLayer(Order);
            try { DrawInner(); }
            catch (Exception e) { Hud.LogOnce("target:" + e.GetType().Name, "Target frame: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        Unit PickTarget(CombatController c, Battle b)
        {
            var h = c.HoveredTarget;
            if (Valid(h, b)) return h;
            var f = Hud.Flow;
            var fh = f != null ? f.HoveredUnit : null;
            if (Valid(fh, b)) return fh;
            var ai = Hud.ActiveAiUnit;
            if (ai != null && ai.Team != b.PlayerTeam && Valid(ai, b)) { sticky = ai; return ai; }
            var me = Hud.ActivePlayerUnit ?? (f != null ? f.Selected : null);
            if (me != null)
            {
                if (Valid(me.AttackTarget, b) && me.AttackTarget.IsHostileTo(me)) { sticky = me.AttackTarget; return sticky; }
                if (me.ComboPoints > 0 && Valid(me.ComboTarget, b)) { sticky = me.ComboTarget; return sticky; }
            }
            if (Valid(sticky, b) && sticky.Team != b.PlayerTeam) return sticky;
            sticky = null;
            return null;
        }

        // a unit stays framed until its death has been shown (HudPresented), not when the killing command is issued
        static bool Valid(Unit u, Battle b) => u != null && !HudPresented.Dead(u) && b.Units.Contains(u);

        void DrawInner()
        {
            HudStyles.Ensure();
            var c = Hud.Combat;
            var b = c != null ? c.Battle : null;
            Hud.FramedTarget = null;
            // after the killing blow the battle is over at once, but the blow is still being animated: keep the frame
            // until the controller has presented everything (the target then reads as dead and is dropped)
            if (b == null || (b.IsOver && c.Finished)) return;
            var t = PickTarget(c, b);
            if (t == null) return;
            Hud.FramedTarget = t;

            bool hostile = t.Team != b.PlayerTeam;
            bool casting = t.Pending != null && t.Pending.Ability != null;
            float h = 96f + (casting ? 26f : 0f);
            var r = new Rect(Mathf.Round(Ui.Width * 0.5f - W * 0.5f), TurnOrderHud.Bottom + 8f, W, h);
            var rank = t.Rank;
            bool elite = hostile && (rank == CreatureRank.Elite || rank == CreatureRank.Rare || rank == CreatureRank.Boss);
            Color rankCol = rank == CreatureRank.Rare ? Hud.C("#d9e1f2") : Ui.Gold;
            if (rank == CreatureRank.Boss) HudDraw.Glow(new Rect(r.x - 40f, r.y - 30f, r.width + 80f, r.height + 60f), new Color(0.9f, 0.25f, 0.2f, 0.22f));
            HudDraw.Frame(r, 0.84f, elite ? new Color(rankCol.r, rankCol.g, rankCol.b, 0.9f) : (Color?)null);
            // no Ui.Block: informational like the status pill (see the header)

            // portrait (+ dragon frame)
            var pr = new Rect(r.x + 10f, r.y + 10f, Portrait, Portrait);
            if (elite) DrawDragonFrame(pr, rankCol);
            HudDraw.Portrait(pr, t, elite ? rankCol : Hud.TeamColor(t), HudPresented.Downed(t));
            // level badge (skull for bosses / far stronger foes)
            var me = Hud.ActivePlayerUnit ?? (Hud.Flow != null ? Hud.Flow.Selected : null);
            bool skull = hostile && (rank == CreatureRank.Boss || (me != null && t.Level >= me.Level + 10));
            var lb = new Rect(pr.xMax - 24f, pr.yMax - 20f, 28f, 24f);
            HudDraw.Fill(lb, new Color(0.08f, 0.06f, 0.13f, 0.95f), 8);
            HudDraw.Ring(lb, new Color(LevelColor(t, me).r, LevelColor(t, me).g, LevelColor(t, me).b, 0.9f), 8);
            if (skull) HudDraw.Glyph(new Rect(lb.x + 5f, lb.y + 3f, 18f, 18f), "glyph_skull", Ui.Bad);
            else HudDraw.Text(lb, level.Get(t.Level), HudStyles.TinyCenter, LevelColor(t, me), false);

            float cx = pr.xMax + 14f, cw = r.xMax - cx - 12f;
            // name + rank/type
            var nameCol = hostile ? Hud.C("#ff9a88") : Hud.UnitColor(t);
            HudDraw.Text(new Rect(cx, r.y + 6f, cw - 120f, 22f), Hud.NameOf(t), HudStyles.Name, nameCol);
            string rankName = hostile ? Hud.RankName(rank) : "";
            string kind = t.Class != null ? t.Class.name : (t.Creature != null ? TypeName(t) : "");
            HudDraw.Text(new Rect(cx, r.y + 7f, cw, 20f), rankName.Length > 0 ? RankLine(t, rank, kind) : kind, HudStyles.SmallRight, elite ? rankCol : Hud.Muted);

            // health
            var hr = new Rect(cx, r.y + 31f, cw, 20f);
            float max = Mathf.Max(1f, t.MaxHealth), hp = HudPresented.Health(t);
            PartyFramesHud.DrawHealth(hr, t, null, null);
            HudDraw.Text(new Rect(hr.x + 6f, hr.y - 1f, hr.width - 12f, hr.height + 2f), health.Get(Mathf.CeilToInt(hp), Mathf.RoundToInt(max)), HudStyles.Tiny, Ui.TextLight);
            HudDraw.Text(new Rect(hr.x + 6f, hr.y - 1f, hr.width - 12f, hr.height + 2f), pctText.Get(Mathf.CeilToInt(hp / max * 100f)), HudStyles.TinyRight, Ui.TextLight);

            // resource
            var res = t.PowerType;
            float maxRes = res != ResourceType.None ? t.MaxResource(res) : 0f;
            if (maxRes > 0f)
            {
                var rr = new Rect(cx, r.y + 54f, cw, 10f);
                float cur = t.GetResource(res);
                HudDraw.Bar(rr, cur / maxRes, Ui.ResourceColor(res));
                if (HudDraw.Hover(rr)) Ui.TooltipFor(rr, HudText.ResourceName(res) + " " + resource.Get(Mathf.FloorToInt(cur), Mathf.RoundToInt(maxRes)));
            }

            float y = r.y + 68f;
            // cast bar (telegraph) + interrupt prompt
            if (casting)
            {
                var p = t.Pending;
                var cr = new Rect(cx, y, cw, 20f);
                var col = Hud.SchoolCol(p.Ability.school);
                HudDraw.Bar(cr, Mathf.Max(0.04f, Hud.PendingProgress(p)), new Color(col.r, col.g, col.b, 0.95f));
                HudDraw.Ring(cr, new Color(col.r, col.g, col.b, HudDraw.Pulse(6f, 0.5f, 1f)), 5);
                HudDraw.Text(new Rect(cr.x + 6f, cr.y - 1f, cr.width - 12f, cr.height + 2f), p.Ability.name, HudStyles.NameSmall, Ui.TextLight);
                HudDraw.Text(new Rect(cr.x + 6f, cr.y - 1f, cr.width - 12f, cr.height + 2f), castLeft.Get(p.RemainingTime), HudStyles.TinyRight, Ui.TextLight);
                if (HudDraw.Hover(cr))
                    Ui.TooltipFor(cr, "<b>" + Hud.NameOf(t) + " is casting " + p.Ability.name + "</b>\nIt resolves at the start of its next turn (" + HudText.Secs(p.RemainingTime) + " s left).\n" +
                                      Ui.Rich("Interrupt it (Kick, Pummel, Counterspell, Earth Shock…) or stun/silence/fear it before then.", Hud.Muted));
                string intr = hostile ? InterruptHint() : "";
                if (intr.Length > 0)
                {
                    var ir = new Rect(cx, y + 22f, cw, 18f);
                    HudDraw.Text(ir, intr, HudStyles.NameSmall, Color.Lerp(Ui.Gold, Ui.Bad, HudDraw.Pulse(7f, 0f, 1f)));
                    y += 4f;
                }
                y += 24f;
            }

            // threat / target line
            string line = AttackLine(t, b, hostile);
            if (line.Length > 0) HudDraw.Rich(new Rect(cx, y, cw, 18f), line, HudStyles.Small);
            if (hostile && me != null && !me.Dead)
            {
                int pct = ThreatPct(b, t, me);
                if (pct >= 0)
                {
                    Color tc = pct >= 100 ? Ui.Bad : pct >= 80 ? Hud.C("#f2b36b") : Ui.Good;
                    var tr = new Rect(cx, y, cw, 18f);
                    HudDraw.Text(tr, threatPct.Get(pct), HudStyles.SmallRight, tc);
                    if (HudDraw.Hover(tr))
                        Ui.TooltipFor(tr, "<b>Threat</b>\n" + Hud.NameOf(me) + " has " + pct + "% of the threat of " + Hud.NameOf(t) + "'s current target.\n" +
                                          Ui.Rich("Enemies switch to someone above 110% (melee) / 130% (ranged) of their target's threat. Tanks taunt to take it back.", Hud.Muted));
                }
            }

            // combo points on this target
            if (me != null && me.ComboPoints > 0 && me.ComboTarget == t)
            {
                for (int i = 0; i < 5; i++)
                    HudDraw.Pip(new Rect(pr.x + 6f + i * 14f, pr.y - 7f, 11f, 11f), Hud.C("#ffcf4d"), i < me.ComboPoints);
            }

            // auras below the frame
            DrawAuras(t, me, r.x + 4f, r.yMax + 4f, W - 8f);
        }

        static void DrawDragonFrame(Rect pr, Color col)
        {
            if (!HudDraw.IsRepaint) return;
            // gold wings either side of the portrait + a thicker ornate ring
            var left = new Rect(pr.x - 22f, pr.y + 4f, 30f, pr.height - 14f);
            var right = new Rect(pr.xMax - 8f, pr.y + 4f, 30f, pr.height - 14f);
            // mirror the left wing around its centre in GUI (virtual) space: old * T(c) * S(-1,1) * T(-c)
            var old = GUI.matrix;
            var c = left.center;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(c.x, c.y, 0f), Quaternion.identity, new Vector3(-1f, 1f, 1f)) *
                         Matrix4x4.TRS(new Vector3(-c.x, -c.y, 0f), Quaternion.identity, Vector3.one);
            HudDraw.Rehide();   // a matrix change recomputes the mouse: keep a covered layer's mouse hidden
            try { HudDraw.Glyph(left, "glyph_wings", col); }
            finally
            {
                GUI.matrix = old;
                HudDraw.Rehide();
            }
            HudDraw.Glyph(right, "glyph_wings", col);
            HudDraw.Ring(new Rect(pr.x - 3f, pr.y - 3f, pr.width + 6f, pr.height + 6f), new Color(col.r, col.g, col.b, 0.9f), 8, true);
        }

        static Color LevelColor(Unit t, Unit me)
        {
            if (me == null || t.Team == me.Team) return Ui.Gold;
            int d = t.Level - me.Level;
            if (d >= 5) return Hud.C("#ff4a3a");
            if (d >= 3) return Hud.C("#ff8a3a");
            if (d >= -2) return Hud.C("#ffe14a");
            if (d >= -7) return Hud.C("#5ee05e");
            return Hud.C("#9d9d9d");
        }

        static readonly Dictionary<CreatureType, string> typeNames = new Dictionary<CreatureType, string>();
        static string TypeName(Unit t)
        {
            var ct = t.CreatureType;
            if (!typeNames.TryGetValue(ct, out var s)) typeNames[ct] = s = UiText.Spaced(ct.ToString());
            return s;
        }

        // "Elite Beast" per framed unit (rebuilt only when the unit or its rank changes: no per-event key string)
        Unit rankLineFor;
        CreatureRank rankLineRank;
        string rankLine = "";
        string RankLine(Unit t, CreatureRank rank, string kind)
        {
            if (t == rankLineFor && rank == rankLineRank && rankLine.Length > 0) return rankLine;
            rankLineFor = t;
            rankLineRank = rank;
            string rn = Hud.RankName(rank);
            rankLine = kind.Length > 0 ? rn + " " + kind : rn;
            return rankLine;
        }

        string AttackLine(Unit t, Battle b, bool hostile)
        {
            Unit target = null;
            try
            {
                if (hostile) target = t.TauntedBy ?? t.AggroTarget ?? b.TopThreat(t);
                else target = t.AttackTarget;
            }
            catch (Exception) { target = null; }
            if (target == null || target.Dead) { attackFor = t; attackOf = null; attackLine = ""; return attackLine; }
            if (attackFor == t && attackOf == target && attackLine.Length > 0) return attackLine;
            attackFor = t;
            attackOf = target;
            string verb = hostile ? (t.TauntedBy == target ? "Taunted by " : "Attacking ") : "Target: ";
            attackLine = verb + Ui.Rich(Hud.NameOf(target), Hud.UnitColor(target));
            return attackLine;
        }

        static int ThreatPct(Battle b, Unit t, Unit me)
        {
            try
            {
                if (t.Threat == null || t.Threat.Count == 0) return -1;
                float mine = b.ThreatOf(t, me);
                var cur = t.AggroTarget ?? b.TopThreat(t);
                float top = cur != null ? b.ThreatOf(t, cur) : 0f;
                if (top <= 0f) return mine > 0f ? 100 : 0;
                return Mathf.RoundToInt(mine / top * 100f);
            }
            catch (Exception) { return -1; }
        }

        string InterruptHint()
        {
            var me = Hud.ActivePlayerUnit;
            if (me == null || Hud.BarUnit != me) return "";
            var list = Hud.BarStatuses;
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                if (st == null || st.Ability == null || !st.Usable || !Hud.HasTag(st.Ability, "Interrupt")) continue;
                string key = Hud.HotkeyOf != null ? Hud.HotkeyOf(st.Ability.id) : "";
                if (interruptFor == st.Ability.id && interruptKey == key) return interruptLine;
                interruptFor = st.Ability.id;
                interruptKey = key;
                interruptLine = "Interrupt now: " + st.Ability.name + (key.Length > 0 ? "  [" + key + "]" : "");
                return interruptLine;
            }
            return "";
        }

        void DrawAuras(Unit t, Unit me, float x, float y, float width)
        {
            auras.Clear();
            var list = t.Auras;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a != null && a.Def != null && !a.Def.hidden && !a.IsPassive && a.IsDebuff) auras.Add(a);
            }
            int debuffs = auras.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a != null && a.Def != null && !a.Def.hidden && !a.IsPassive && !a.IsDebuff) auras.Add(a);
            }
            if (auras.Count == 0) return;
            int perRow = Mathf.Max(1, (int)((width + 4f) / (AuraSize + 4f)));
            int n = Mathf.Min(auras.Count, perRow * 2);
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                var ar = new Rect(x + col * (AuraSize + 4f), y + row * (AuraSize + 5f), AuraSize, AuraSize);
                var a = auras[i];
                bool mine = me != null && a.Caster == me;
                if (mine) HudDraw.Glow(new Rect(ar.x - 6f, ar.y - 6f, ar.width + 12f, ar.height + 12f), new Color(1f, 0.9f, 0.5f, 0.45f));
                PartyFramesHud.DrawAuraIcon(ar, a, i < debuffs, null);
            }
        }
    }
}
