// Talents (N): the selected member's three trees side by side — 7 tiers × 4 columns, talent icons with "x/y" ranks,
// prerequisite arrows (gold once satisfied), locked tiers dimmed with their point requirement, points per tree and
// available. Left click learns a rank (Session.LearnTalent); tooltips show every rank with the current one highlighted
// and why a talent cannot be learned yet. "Recommended build" spends free points on the class/companion build; "Reset"
// appears only while a trainer's respec window is open (Session.Respec).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class TalentsPanel : PanelWindow
    {
        public override string Id => UiPanels.Talents;
        public override int Order => 140;

        static Unit requested;

        /// <summary>Makes the panel show this member next time it draws.</summary>
        public static void SelectMember(Unit u) => requested = u;

        Unit member;
        readonly List<TalentTreeDef> trees = new List<TalentTreeDef>();
        ClassDef treesFor;
        readonly Dictionary<string, string> tips = new Dictionary<string, string>();
        readonly Dictionary<string, int> tipStamp = new Dictionary<string, int>();
        readonly Dictionary<string, Rect> cellRects = new Dictionary<string, Rect>();
        string headerText = "";
        int headerStamp = -1;
        int lockFrame;

        void EnsureTrees(Unit u)
        {
            var cls = u != null ? u.Class : null;
            if (cls == treesFor) return;
            treesFor = cls;
            trees.Clear();
            tips.Clear();
            tipStamp.Clear();
            var db = PanelKit.Db;
            if (cls == null || db == null || cls.talentTrees == null) return;
            foreach (var id in cls.talentTrees)
                if (!string.IsNullOrEmpty(id) && db.TalentTrees.TryGetValue(id, out var t) && t != null) trees.Add(t);
        }

        static Color TreeColor(TalentTreeDef t)
        {
            string i = (t.icon ?? "").ToLowerInvariant();
            switch (i)
            {
                case "fire": return Ui.SchoolColor(School.Fire);
                case "frost": case "water_drop": return Ui.SchoolColor(School.Frost);
                case "arcane": return Ui.SchoolColor(School.Arcane);
                case "holy": case "halo": return Ui.SchoolColor(School.Holy);
                case "shadow": case "skull": case "demon": return Ui.SchoolColor(School.Shadow);
                case "paw": case "nature": case "poison": case "lightning": case "trap": return Ui.SchoolColor(School.Nature);
                case "rage": return Ui.Rage;
                case "stealth": return Ui.Hex("#8f86a8");
                default: return Ui.SchoolColor(School.Physical);
            }
        }

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            if (requested != null) { PanelKit.Member = requested; requested = null; }
            member = PanelKit.Member;
            var u = member;
            EnsureTrees(u);
            var r = PanelKit.Centered(Mathf.Min(1340f, Ui.Width - 40f), Mathf.Min(900f, Ui.Height - 60f), -10f);
            var c = Chrome(r, "Talents");
            var nm = PanelKit.MemberTabs(new Rect(c.x, c.y - 4f, c.width * 0.5f, 48f), u, PanelKit.Characters, 46f);
            if (nm != u) { PanelKit.Member = nm; u = member = nm; EnsureTrees(u); }
            if (u == null || u.Class == null) { PanelKit.Label(c, "No one to show.", PanelKit.TextCenter); return; }

            int avail = s.TalentPointsAvailable(u);
            DrawHeader(new Rect(c.x + c.width * 0.5f, c.y - 6f, c.width * 0.5f, 52f), u, avail);

            float top = c.y + 56f;
            float footerH = 64f;
            var area = new Rect(c.x, top, c.width, c.yMax - top - footerH);
            if (trees.Count == 0) { PanelKit.Label(area, "This class has no talents.", PanelKit.TextCenter); return; }
            const float gap = 16f;
            float tw = (area.width - gap * (trees.Count - 1)) / trees.Count;
            cellRects.Clear();
            for (int i = 0; i < trees.Count; i++)
                DrawTree(new Rect(area.x + i * (tw + gap), area.y, tw, area.height), trees[i], u, avail, s);
            DrawFooter(new Rect(c.x, c.yMax - footerH + 10f, c.width, footerH - 10f), u, avail, s);
        }

        void DrawHeader(Rect r, Unit u, int avail)
        {
            int stamp = u.Id * 1000 + u.Level * 10 + Mathf.Min(9, avail);
            if (stamp != headerStamp)
            {
                headerStamp = stamp;
                headerText = u.Level < 10
                    ? $"<b>{PanelKit.NameOf(u)}</b>, level {u.Level} {u.Class.name} — talents unlock at level 10"
                    : $"<b>{PanelKit.NameOf(u)}</b>, level {u.Level} {u.Class.name}";
            }
            PanelKit.Label(new Rect(r.x, r.y, r.width - 210f, r.height), headerText, PanelKit.Text);
            var plate = new Rect(r.xMax - 200f, r.y + 6f, 200f, 40f);
            PanelKit.Rounded(plate, avail > 0 ? new Color(1f, 0.82f, 0.4f, 0.55f) : new Color(0.17f, 0.13f, 0.22f, 0.1f));
            PanelKit.Label(plate, avail > 0 ? $"<b>{avail}</b> point{(avail == 1 ? "" : "s")} to spend" : "No points to spend", PanelKit.TextCenter);
        }

        void DrawTree(Rect r, TalentTreeDef tree, Unit u, int avail, GameSession s)
        {
            var col = TreeColor(tree);
            int spent = Progression.PointsInTree(u, tree.id);
            // background
            PanelKit.Rounded(r, new Color(col.r * 0.5f, col.g * 0.5f, col.b * 0.5f, 0.16f));
            if (!string.IsNullOrEmpty(tree.background) && ArtLibrary.HasRealTexture(tree.background))
                PanelKit.Tex(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), ArtLibrary.Texture(tree.background), new Color(1f, 1f, 1f, 0.35f), ScaleMode.ScaleAndCrop);
            var gtex = Ui.GlyphTexture(tree.icon);
            if (gtex != null) PanelKit.Tex(new Rect(r.center.x - 120f, r.center.y - 110f, 240f, 240f), gtex, new Color(col.r, col.g, col.b, 0.10f), ScaleMode.ScaleToFit);
            PanelKit.Outline(r, new Color(col.r * 0.7f, col.g * 0.7f, col.b * 0.7f, 0.5f));
            // header
            Ui.Icon(new Rect(r.x + 12f, r.y + 10f, 40f, 40f), tree.icon, col);
            PanelKit.Label(new Rect(r.x + 62f, r.y + 8f, r.width - 130f, 30f), tree.name, PanelKit.Heading);
            PanelKit.Label(new Rect(r.xMax - 70f, r.y + 10f, 58f, 30f), PanelKit.CountText(spent), Ui.NumberStyle(24, TextAnchor.MiddleRight), Ui.Ink);
            if (!string.IsNullOrEmpty(tree.description)) Ui.TooltipFor(new Rect(r.x, r.y, r.width, 56f), "<b>" + tree.name + "</b>\n" + tree.description);

            // grid
            var grid = new Rect(r.x + 8f, r.y + 62f, r.width - 16f, r.height - 70f);
            float cellW = grid.width / 4f, cellH = grid.height / 7f;
            float icon = Mathf.Min(54f, Mathf.Min(cellW, cellH) - 22f);
            // tier locks
            for (int tier = 2; tier <= 7; tier++)
            {
                int need = 5 * (tier - 1);
                if (spent >= need) continue;
                var tr = new Rect(grid.x, grid.y + (tier - 1) * cellH, grid.width, cellH);
                if (PanelKit.IsRepaint) PanelKit.Rect(new Rect(tr.x + 4f, tr.y + 2f, tr.width - 8f, tr.height - 4f), new Color(0.12f, 0.1f, 0.16f, 0.06f));
                PanelKit.Label(new Rect(tr.x - 2f, tr.y + 2f, 26f, 18f), PanelKit.CountText(need), Ui.NumberStyle(12, TextAnchor.UpperLeft), new Color(0.17f, 0.13f, 0.22f, 0.45f));
            }
            // cell rects first (arrows are drawn under the icons)
            foreach (var t in tree.talents)
            {
                if (t == null) continue;
                float cx = grid.x + (Mathf.Clamp(t.column, 0, 3) + 0.5f) * cellW;
                float cy = grid.y + (Mathf.Clamp(t.tier, 1, 7) - 0.5f) * cellH;
                cellRects[t.id] = new Rect(cx - icon * 0.5f, cy - icon * 0.5f - 4f, icon, icon);
            }
            foreach (var t in tree.talents)
                if (t != null && !string.IsNullOrEmpty(t.requires) && cellRects.TryGetValue(t.requires, out var from) && cellRects.TryGetValue(t.id, out var to))
                {
                    var pre = PanelKit.Db.Talent(t.requires);
                    bool met = pre != null && u.TalentRank(pre.id) >= pre.maxRank;
                    Arrow(from, to, met ? Ui.GoldDeep : new Color(0.17f, 0.13f, 0.22f, 0.35f));
                }
            foreach (var t in tree.talents)
            {
                if (t == null || !cellRects.TryGetValue(t.id, out var cr)) continue;
                DrawTalent(cr, t, u, avail, spent, s);
            }
        }

        static void Arrow(Rect from, Rect to, Color col)
        {
            if (!PanelKit.IsRepaint) return;
            const float th = 5f;
            if (Mathf.Abs(from.center.x - to.center.x) < 1f)
            {
                float y0 = from.yMax + 2f, y1 = to.y - 10f;
                if (y1 > y0) PanelKit.Rect(new Rect(from.center.x - th * 0.5f, y0, th, y1 - y0), col);
                ArrowHeadAt(new Vector2(to.center.x, to.y - 6f), 90f, col);
            }
            else if (Mathf.Abs(from.center.y - to.center.y) < 1f)
            {
                bool right = to.center.x > from.center.x;
                float x0 = right ? from.xMax + 2f : to.xMax + 10f, x1 = right ? to.x - 10f : from.x - 2f;
                if (x1 > x0) PanelKit.Rect(new Rect(x0, from.center.y - th * 0.5f, x1 - x0, th), col);
                ArrowHeadAt(new Vector2(right ? to.x - 6f : to.xMax + 6f, to.center.y), right ? 0f : 180f, col);
            }
            else
            {
                // elbow: across at the prerequisite's height, then down
                float x0 = Mathf.Min(from.center.x, to.center.x), x1 = Mathf.Max(from.center.x, to.center.x);
                PanelKit.Rect(new Rect(x0, from.center.y - th * 0.5f, x1 - x0, th), col);
                float y0 = from.center.y, y1 = to.y - 10f;
                if (y1 > y0) PanelKit.Rect(new Rect(to.center.x - th * 0.5f, y0, th, y1 - y0), col);
                ArrowHeadAt(new Vector2(to.center.x, to.y - 6f), 90f, col);
            }
        }

        static void ArrowHeadAt(Vector2 p, float angle, Color col)
        {
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, p * Ui.Scale);
            PanelKit.Rehide();
            PanelKit.Tex(new Rect(p.x - 9f, p.y - 9f, 18f, 18f), PanelArt.ArrowHead, col);
            PanelKit.SetMatrix(old);
        }

        void DrawTalent(Rect cr, TalentDef t, Unit u, int avail, int treeSpent, GameSession s)
        {
            int rank = u.TalentRank(t.id);
            int max = Mathf.Max(1, t.maxRank);
            bool tierOk = treeSpent >= 5 * (t.tier - 1);
            bool preOk = true;
            if (!string.IsNullOrEmpty(t.requires))
            {
                var pre = PanelKit.Db.Talent(t.requires);
                preOk = pre == null || u.TalentRank(pre.id) >= pre.maxRank;
            }
            bool maxed = rank >= max;
            bool canLearn = !maxed && avail > 0 && tierOk && preOk;
            bool hover = PanelKit.Hover(cr);
            Color frame = maxed ? Ui.Gold : rank > 0 ? Ui.Good : canLearn ? Ui.Hex("#9fe39a") : new Color(0.55f, 0.52f, 0.6f, 1f);
            if (canLearn && PanelKit.IsRepaint)
            {
                float pulse = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f + t.column);
                PanelKit.Tex(new Rect(cr.x - 12f, cr.y - 12f, cr.width + 24f, cr.height + 24f), ProceduralArt.Glow, new Color(0.6f, 1f, 0.55f, pulse));
            }
            if (maxed) PanelKit.Tex(new Rect(cr.x - 10f, cr.y - 10f, cr.width + 20f, cr.height + 20f), ProceduralArt.Glow, new Color(1f, 0.82f, 0.4f, 0.55f));
            bool dim = rank == 0 && !canLearn;
            Ui.Icon(cr, t.icon, frame, 0f, dim);
            if (hover) PanelKit.Outline(new Rect(cr.x - 2f, cr.y - 2f, cr.width + 4f, cr.height + 4f), Ui.Ink);
            // rank badge
            var badge = new Rect(cr.xMax - 26f, cr.yMax - 12f, 34f, 20f);
            PanelKit.Rounded(badge, new Color(0.10f, 0.08f, 0.15f, 0.92f));
            var bc = maxed ? Ui.Gold : rank > 0 ? Ui.Good : canLearn ? Ui.TextLight : Ui.TextMuted;
            PanelKit.Label(badge, RankText(rank, max), Ui.NumberStyle(13), bc);
            var hitR = new Rect(cr.x - 6f, cr.y - 6f, cr.width + 12f, cr.height + 18f);
            Ui.TooltipFor(hitR, TipOf(t, u, rank, avail, treeSpent));
            GameInput.BlockRectGui(hitR);
            if (PanelKit.Click(hitR, out int button) && button == 0 && Time.frameCount > lockFrame)
            {
                if (maxed) return;
                lockFrame = Time.frameCount + 1;
                var who = u;
                string id = t.id;
                string name = t.name;
                PanelKit.Do(() =>
                {
                    var ss = PanelKit.Sess;
                    if (ss == null) return;
                    if (PanelKit.Try(() => ss.LearnTalent(who, id))) Ui.Sfx?.Invoke("buff");
                });
            }
        }

        static readonly string[] rankTexts = new string[64];

        static string RankText(int rank, int max)
        {
            int k = Mathf.Clamp(rank, 0, 7) * 8 + Mathf.Clamp(max, 0, 7);
            return rankTexts[k] ?? (rankTexts[k] = rank + "/" + max);
        }

        string TipOf(TalentDef t, Unit u, int rank, int avail, int treeSpent)
        {
            int stamp = rank + 8 * Mathf.Min(avail, 60) + 1000 * treeSpent + 100000 * (u.Id % 1000);
            if (tips.TryGetValue(t.id, out var tip) && tipStamp.TryGetValue(t.id, out var st) && st == stamp) return tip;
            var sb = new System.Text.StringBuilder();
            sb.Append(UiText.Talent(t, rank));
            if (rank >= t.maxRank) sb.Append('\n').Append(Ui.Rich("Fully learned", Ui.Gold));
            else
            {
                string why = null;
                try { why = Progression.CannotLearnTalent(u, t.id); } catch (Exception) { }
                sb.Append('\n').Append(why == null ? Ui.Rich(rank == 0 ? "Click to learn" : "Click to learn the next rank", Ui.Good) : Ui.Rich(why, Ui.Bad));
            }
            tip = sb.ToString();
            tips[t.id] = tip;
            tipStamp[t.id] = stamp;
            return tip;
        }

        void DrawFooter(Rect r, Unit u, int avail, GameSession s)
        {
            PanelKit.HLine(r.x, r.y - 6f, r.width);
            string build = u.Companion != null && u.Companion.preferredTalents != null && u.Companion.preferredTalents.Length > 0 ? "their favourite build" : "the recommended build";
            const string resetHint = "A class trainer can reset your talents for a fee.";
            bool combat = PanelKit.InCombat;
            if (avail != buildTipPoints || u != buildTipFor)
            {
                buildTipPoints = avail;
                buildTipFor = u;
                buildTip = avail > 0 ? $"Spend the {avail} free point{(avail == 1 ? "" : "s")} on {build}." : "No points to spend.";
            }
            if (Ui.Btn(new Rect(r.x, r.y + 4f, 260f, 46f), "Recommended build", Ui.Button, avail > 0 && !combat, combat ? "Not during combat." : buildTip))
            {
                var who = u;
                int pts = avail;
                ConfirmScreen.Ask("Spend talent points?", $"Spend {pts} point{(pts == 1 ? "" : "s")} on {build} for {PanelKit.NameOf(who)}?", "Spend", () =>
                {
                    var ss = PanelKit.Sess;
                    if (ss == null) return;
                    int n = ss.AutoAllocateTalents(who);
                    if (n > 0) { Ui.Sfx?.Invoke("buff"); PanelKit.Notice($"{n} talent point{(n == 1 ? "" : "s")} spent.", false); }
                    else PanelKit.Notice("No points could be spent.");
                });
            }
            if (!string.IsNullOrEmpty(s.ActiveRespecNpc))
            {
                int spent = Progression.TalentPointsSpent(u);
                int cost = s.RespecCost(u);
                bool afford = s.Gold >= cost;
                if (Ui.Btn(new Rect(r.x + 276f, r.y + 4f, 220f, 46f), "Reset talents", Ui.ButtonDark, spent > 0 && afford && !combat,
                        spent == 0 ? "There is nothing to unlearn." : afford ? "Unlearn every talent for " + Inventory.FormatMoney(cost) + "." : "Not enough money."))
                {
                    var who = u;
                    ConfirmScreen.Ask("Reset talents?", $"{PanelKit.NameOf(who)} forgets all {spent} talents for {Inventory.FormatMoney(cost)}.", "Reset", () =>
                    {
                        var ss = PanelKit.Sess;
                        if (ss != null) PanelKit.Try(() => ss.Respec(who), "Talents reset.");
                    }, "Keep them", null, true);
                }
            }
            else PanelKit.Label(new Rect(r.x + 276f, r.y + 6f, 420f, 44f), resetHint, PanelKit.TextMutedSmall);
            PanelKit.Label(new Rect(r.xMax - 520f, r.y + 6f, 520f, 40f), SpentText(u), PanelKit.TextSmallRight);
        }

        int buildTipPoints = -1;
        Unit buildTipFor;
        string buildTip = "";
        string spentText = "";
        int spentStamp = -1;

        string SpentText(Unit u)
        {
            int stamp = u.Id * 7919;
            foreach (var kv in u.Talents) stamp += kv.Value * 31 + kv.Key.Length;
            if (stamp == spentStamp) return spentText;
            spentStamp = stamp;
            var sb = new System.Text.StringBuilder("Points spent:  ");
            for (int i = 0; i < trees.Count; i++)
            {
                if (i > 0) sb.Append("  ·  ");
                sb.Append(trees[i].name).Append(' ').Append("<b>").Append(Progression.PointsInTree(u, trees[i].id)).Append("</b>");
            }
            spentText = sb.ToString();
            return spentText;
        }
    }
}
