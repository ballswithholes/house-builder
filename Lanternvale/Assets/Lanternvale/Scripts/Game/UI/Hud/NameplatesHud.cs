// Nameplates & labels (drawn first, under every other HUD layer):
//  * combat: small health bars over enemies (level/rank, name when hovered/targeted/elite, telegraphed cast bar),
//    a slim bar over hovered allies, the framed target outlined;
//  * exploration: GameFlow.HoveredLabel near HoveredLabelWorld coloured by HoveredKind with an interaction prompt
//    ("Talk", "Open", "Travel", "Attack" — or the armed opener), party member names on hover.
// Anchors: UnitView.NameplatePosition → CameraRig.WorldToGui / Ui.Scale.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class NameplatesHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderNameplates;
        public bool Visible => Hud.WorldHud;
        public bool Modal => false;

        const float PlateW = 96f, PlateH = 9f;

        readonly GUIContent measure = new GUIContent();
        readonly Dictionary<string, float> widthCache = new Dictionary<string, float>();
        readonly Dictionary<Unit, string> levelNames = new Dictionary<Unit, string>();
        string openerName = "", openerId = "";

        public void Tick(float dt)
        {
            if (!GameFlow.HasGame && levelNames.Count > 0) levelNames.Clear();
            if (Hud.Battle == null && levelNames.Count > 64) levelNames.Clear();
        }

        public void Draw()
        {
            if (!HudDraw.IsRepaint) return;   // purely visual: nothing to do for input events
            try
            {
                HudStyles.Ensure();
                var rig = CameraRig.Instance;
                var f = Hud.Flow;
                if (rig == null || rig.Cam == null || f == null) return;
                var b = Hud.Battle;
                if (b != null) DrawCombat(b, rig, f);
                else DrawExploration(rig, f);
            }
            catch (Exception e) { Hud.LogOnce("plates:" + e.GetType().Name, "Nameplates: " + e); }
        }

        Vector2 ToGui(CameraRig rig, Vector2 world)
        {
            var g = rig.WorldToGui(world);
            float s = Ui.Scale <= 0f ? 1f : Ui.Scale;
            return new Vector2(g.x / s, g.y / s);
        }

        bool OnScreen(Vector2 p) => p.x > -60f && p.x < Ui.Width + 60f && p.y > -40f && p.y < Ui.Height + 40f;

        // ================================================================ combat

        void DrawCombat(Battle b, CameraRig rig, GameFlow f)
        {
            var units = b.Units;
            var hovered = Hud.Combat != null ? Hud.Combat.HoveredTarget ?? f.HoveredUnit : f.HoveredUnit;
            var framed = Hud.FramedTarget;
            var active = b.ActiveUnit;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.Dead) continue;
                bool hostile = u.Team != b.PlayerTeam;
                bool show = hostile ? !u.IsTotem || u == hovered : (u == hovered && !u.IsTotem);
                if (!show) continue;
                UnitView v;
                try { v = f.ViewOf(u); } catch (Exception) { v = null; }
                if (v == null || !v.Visible || v.IsDead) continue;
                var p = ToGui(rig, v.NameplatePosition);
                if (!OnScreen(p)) continue;
                DrawPlate(p, u, hostile, u == framed, u == hovered, u == active);
            }
        }

        void DrawPlate(Vector2 p, Unit u, bool hostile, bool framed, bool hovered, bool active)
        {
            float w = PlateW, x = Mathf.Round(p.x - w * 0.5f), y = Mathf.Round(p.y - 22f);
            var rank = u.Rank;
            bool elite = hostile && (rank == CreatureRank.Elite || rank == CreatureRank.Rare || rank == CreatureRank.Boss);
            if (elite) { w += 16f; x -= 8f; }
            var bar = new Rect(x, y, w, PlateH);
            float pct = u.MaxHealth > 0f ? Mathf.Clamp01(u.Health / u.MaxHealth) : 0f;
            if (framed) HudDraw.Glow(new Rect(bar.x - 14f, bar.y - 10f, bar.width + 28f, bar.height + 20f), new Color(1f, 0.9f, 0.5f, 0.45f));
            HudDraw.Bar(bar, pct, hostile ? Hud.C("#d9483b") : Hud.HealthColor(pct), 0.85f);
            float absorb = Hud.AbsorbOf(u);
            if (absorb > 0f && u.MaxHealth > 0f) HudDraw.BarSegment(bar, pct, Mathf.Min(1f, (u.Health + absorb) / u.MaxHealth), Hud.Absorb);
            Color edge = framed ? Ui.Gold : active ? new Color(1f, 0.85f, 0.45f, 0.9f) : elite ? new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.7f) : new Color(0f, 0f, 0f, 0.6f);
            HudDraw.Ring(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), edge, 3);

            // level / rank badge left of the bar
            if (hostile)
            {
                string lv = LevelName(u);
                var lr = new Rect(bar.x - 30f, bar.y - 4f, 28f, 16f);
                HudDraw.Text(lr, lv, HudStyles.TinyRight, elite ? Ui.Gold : Ui.TextLight);
            }
            // name above when it matters
            if (hovered || framed || elite || active)
            {
                var nr = new Rect(p.x - 110f, bar.y - 18f, 220f, 16f);
                HudDraw.Text(nr, Hud.NameOf(u), HudStyles.TinyCenter, hostile ? Hud.C("#ffb0a2") : Hud.UnitColor(u));
            }
            // telegraphed cast
            var pend = u.Pending;
            if (pend != null && pend.Ability != null)
            {
                var cr = new Rect(bar.x, bar.yMax + 3f, bar.width, 7f);
                var col = Hud.SchoolCol(pend.Ability.school);
                float total = Hud.PendingTotal(pend);
                HudDraw.Bar(cr, Mathf.Max(0.05f, 1f - Mathf.Clamp01(pend.RemainingTime / total)), col, 0.85f);
                HudDraw.Ring(new Rect(cr.x - 1f, cr.y - 1f, cr.width + 2f, cr.height + 2f), new Color(col.r, col.g, col.b, HudDraw.Pulse(7f, 0.4f, 1f)), 3);
                HudDraw.Text(new Rect(p.x - 110f, cr.yMax, 220f, 15f), pend.Ability.name, HudStyles.TinyCenter, Color.Lerp(col, Color.white, 0.4f));
            }
        }

        string LevelName(Unit u)
        {
            if (levelNames.TryGetValue(u, out var s) && s != null && s.Length > 0 && LevelMatches(s, u)) return s;
            s = HudText.Int(u.Level);
            var rank = u.Rank;
            if (rank == CreatureRank.Boss) s = "??";
            else if (rank == CreatureRank.Elite || rank == CreatureRank.Rare) s = HudText.Int(u.Level) + "+";
            levelNames[u] = s;
            return s;
        }

        static bool LevelMatches(string s, Unit u) => s == "??" || s.StartsWith(HudText.Int(u.Level), StringComparison.Ordinal);

        // ================================================================ exploration

        void DrawExploration(CameraRig rig, GameFlow f)
        {
            var kind = f.HoveredKind;
            if (kind == HoverKind.None) return;
            var pos = ToGui(rig, f.HoveredLabelWorld);
            string label = f.HoveredLabel;
            Color col;
            string prompt;
            switch (kind)
            {
                case HoverKind.PartyMember:
                {
                    var u = f.HoveredUnit;
                    if (u == null) return;
                    label = Hud.NameOf(u);
                    col = Hud.UnitColor(u);
                    prompt = f.Selected == u ? "" : "Select";
                    if (Hud.FieldPick != null) prompt = "";
                    break;
                }
                case HoverKind.Npc:
                    col = Hud.C("#ffe08a");
                    prompt = "Talk";
                    break;
                case HoverKind.Enemy:
                    col = Hud.C("#ff9a88");
                    prompt = OpenerPrompt(f);
                    if (f.HoveredUnit != null && string.IsNullOrEmpty(label)) label = Hud.NameOf(f.HoveredUnit);
                    break;
                case HoverKind.Object:
                    col = Hud.C("#d9e8ff");
                    prompt = ObjectPrompt(label);
                    break;
                default:
                    return;
            }
            if (string.IsNullOrEmpty(label)) return;
            float w = Measure(label, HudStyles.Label18) + 26f;
            float pw = string.IsNullOrEmpty(prompt) ? 0f : Measure(prompt, HudStyles.Small) + 34f;
            float total = Mathf.Max(w, pw);
            var r = new Rect(Mathf.Round(pos.x - total * 0.5f), Mathf.Round(pos.y - 34f - (pw > 0f ? 20f : 0f)), total, 28f);
            r.x = Mathf.Clamp(r.x, 6f, Ui.Width - r.width - 6f);
            r.y = Mathf.Clamp(r.y, 6f, Ui.Height - 60f);
            HudDraw.Fill(r, new Color(0.09f, 0.07f, 0.15f, 0.78f), 8);
            HudDraw.Ring(r, new Color(col.r, col.g, col.b, 0.55f), 8);
            HudDraw.Text(r, label, HudStyles.Center, col);
            if (pw > 0f)
            {
                var pr = new Rect(Mathf.Round(r.center.x - pw * 0.5f), r.yMax + 2f, pw, 20f);
                HudDraw.Fill(pr, new Color(0.09f, 0.07f, 0.15f, 0.62f), 8);
                HudDraw.Glyph(new Rect(pr.x + 6f, pr.y + 3f, 14f, 14f), "glyph_hand", new Color(1f, 1f, 1f, 0.8f));
                HudDraw.Text(new Rect(pr.x + 20f, pr.y, pr.width - 24f, pr.height), prompt, HudStyles.SmallCenter, Hud.Muted);
            }
        }

        string OpenerPrompt(GameFlow f)
        {
            string id = f.PendingOpener;
            if (string.IsNullOrEmpty(id)) return "Attack";
            if (id != openerId)
            {
                openerId = id;
                var a = Hud.Db != null ? Hud.Db.Ability(id) : null;
                var caster = f.PendingOpenerCaster;
                openerName = "Open with " + (a != null ? a.name : id) + (caster != null ? " (" + Hud.NameOf(caster) + ")" : "");
            }
            return openerName;
        }

        static string ObjectPrompt(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            if (label.IndexOf("Locked", StringComparison.OrdinalIgnoreCase) >= 0) return "Pick the lock";
            if (label.IndexOf("Empty", StringComparison.OrdinalIgnoreCase) >= 0) return "";
            if (label.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0) return "Open";
            if (label.StartsWith("To ", StringComparison.Ordinal)) return "Travel";
            return "Inspect";
        }

        float Measure(string text, GUIStyle st)
        {
            if (widthCache.TryGetValue(text, out var w)) return w;
            if (widthCache.Count > 256) widthCache.Clear();
            measure.text = text;
            w = st.CalcSize(measure).x;
            widthCache[text] = w;
            return w;
        }
    }
}
