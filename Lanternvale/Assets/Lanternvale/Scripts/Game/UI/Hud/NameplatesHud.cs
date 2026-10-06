// Nameplates & labels (drawn first, under every other HUD layer):
//  * combat: small health bars over enemies (level/rank, name when hovered/targeted/elite, telegraphed cast bar),
//    a slim bar over hovered allies, the framed target outlined;
//  * exploration: GameFlow.HoveredLabel near HoveredLabelWorld coloured by HoveredKind with an interaction prompt
//    ("Talk", "Open", "Travel", "Attack" — or the armed opener), party member names on hover ("Select"; "Talk" over the
//    selected companion, GameFlow.CanTalkTo); a hovered NPC with a quest marker gets a quest line above its name
//    ("Quest: …", "Turn in: …"; GameFlow.HoveredNpcId → Session.QuestMarkerOf). Encounter enemies get a plate from
//    Session.PreviewEncounter (GameFlow.HoveredEnemy/HoveredEncounter): name, WoW-coloured level badge (the level the battle will scale it to;
//    "??" for bosses), elite/rare winged mark or boss skull, and the group size.
// Anchors: UnitView.NameplatePosition (3D world point) → CameraRig.WorldToGui / Ui.Scale; anchors behind the
// perspective camera (CameraRig.IsInFront false) get no plate.
// Combat plates read health/death as presented (HudPresented): a plate stays until the death is shown.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.World;
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

        Vector2 ToGui(CameraRig rig, Vector3 world)
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
                if (u == null || HudPresented.Dead(u)) continue;
                bool hostile = u.Team != b.PlayerTeam;
                bool show = hostile ? !u.IsTotem || u == hovered : (u == hovered && !u.IsTotem);
                if (!show) continue;
                UnitView v;
                try { v = f.ViewOf(u); } catch (Exception) { v = null; }
                if (v == null || !v.Visible || v.IsDead) continue;
                if (!rig.IsInFront(v.NameplatePosition)) continue;   // behind the camera: no meaningful projection
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
            float hp = HudPresented.Health(u);
            float pct = u.MaxHealth > 0f ? Mathf.Clamp01(hp / u.MaxHealth) : 0f;
            if (framed) HudDraw.Glow(new Rect(bar.x - 14f, bar.y - 10f, bar.width + 28f, bar.height + 20f), new Color(1f, 0.9f, 0.5f, 0.45f));
            HudDraw.Bar(bar, pct, hostile ? Hud.C("#d9483b") : Hud.HealthColor(pct), 0.85f);
            float absorb = Hud.AbsorbOf(u);
            if (absorb > 0f && u.MaxHealth > 0f) HudDraw.BarSegment(bar, pct, Mathf.Min(1f, (hp + absorb) / u.MaxHealth), Hud.Absorb);
            Color edge = framed ? Ui.Gold : active ? new Color(1f, 0.85f, 0.45f, 0.9f) : elite ? new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.7f) : new Color(0f, 0f, 0f, 0.6f);
            HudDraw.Ring(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), edge, 3);

            // level / rank badge left of the bar
            if (hostile)
            {
                string lv = LevelName(u);
                var lr = new Rect(bar.x - 32f, bar.y - 5f, 30f, 18f);
                HudDraw.Text(lr, lv, HudStyles.TinyRight, elite ? Ui.Gold : Ui.TextLight);
            }
            // name above when it matters
            if (hovered || framed || elite || active)
            {
                var nr = new Rect(p.x - 110f, bar.y - 20f, 220f, 18f);
                HudDraw.Text(nr, Hud.NameOf(u), HudStyles.TinyCenter, hostile ? Hud.C("#ffb0a2") : Hud.UnitColor(u));
            }
            // telegraphed cast
            var pend = u.Pending;
            if (pend != null && pend.Ability != null)
            {
                var cr = new Rect(bar.x, bar.yMax + 3f, bar.width, 7f);
                var col = Hud.SchoolCol(pend.Ability.school);
                HudDraw.Bar(cr, Mathf.Max(0.05f, Hud.PendingProgress(pend)), col, 0.85f);
                HudDraw.Ring(new Rect(cr.x - 1f, cr.y - 1f, cr.width + 2f, cr.height + 2f), new Color(col.r, col.g, col.b, HudDraw.Pulse(7f, 0.4f, 1f)), 3);
                HudDraw.Text(new Rect(p.x - 110f, cr.yMax, 220f, 18f), pend.Ability.name, HudStyles.TinyCenter, Color.Lerp(col, Color.white, 0.4f));
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
            if (!rig.IsInFront(f.HoveredLabelWorld)) return;
            var pos = ToGui(rig, f.HoveredLabelWorld);
            string label = f.HoveredLabel;
            Color col;
            string prompt;
            QuestMarkerInfo quest = null;
            switch (kind)
            {
                case HoverKind.PartyMember:
                {
                    var u = f.HoveredUnit;
                    if (u == null) return;
                    label = Hud.NameOf(u);
                    col = Hud.UnitColor(u);
                    // a click selects; the selected companion clicked again is talked to (GameFlow.ClickView → TalkToCompanion)
                    prompt = f.Selected != u ? "Select" : f.CanTalkTo(u) ? "Talk" : "";
                    if (Hud.FieldPick != null) prompt = "";
                    break;
                }
                case HoverKind.Npc:
                    col = Hud.C("#ffe08a");
                    prompt = "Talk";
                    quest = QuestOf(f.HoveredNpcId);
                    break;
                case HoverKind.Enemy:
                    col = Hud.C("#ff9a88");
                    prompt = OpenerPrompt(f);
                    if (f.HoveredUnit == null && f.HoveredEnemy != null) { DrawEnemyPreview(pos, f, prompt); return; }
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
            if (quest != null) DrawQuestLine(r, quest);
        }

        // ---------------------------------------------------------------- NPC quest line ("Quest: …" / "Turn in: …")

        string questNpc = "", questText = "";
        int questVersion = -1;
        QuestMarkerInfo questInfo;

        /// <summary>The hovered NPC's best quest marker (cached per QuestMarkersVersion), or null when it has none.</summary>
        QuestMarkerInfo QuestOf(string npcId)
        {
            var s = Hud.Session;
            if (s == null || string.IsNullOrEmpty(npcId)) return null;
            int v = s.QuestMarkersVersion;
            if (npcId != questNpc || v != questVersion)
            {
                questNpc = npcId;
                questVersion = v;
                var m = s.QuestMarkerOf(npcId);
                questInfo = m.IsNone ? null : m;
                questText = questInfo != null ? questInfo.Describe() : "";
                int more = questInfo != null ? s.QuestMarkersOf(npcId).Count - 1 : 0;
                if (more > 0) questText += "  (+" + more + ")";
            }
            return questInfo;
        }

        /// <summary>A slim plate above the name: the marker glyph in its colour, then the quest line.</summary>
        void DrawQuestLine(Rect nameRect, QuestMarkerInfo q)
        {
            if (string.IsNullOrEmpty(questText)) return;
            var gc = q.Yellow ? Hud.C("#ffd23a") : Hud.C("#b4b4b8");
            float w = Measure(questText, HudStyles.Small) + 34f;
            var qr = new Rect(Mathf.Round(nameRect.center.x - w * 0.5f), nameRect.y - 24f, w, 21f);
            qr.x = Mathf.Clamp(qr.x, 6f, Ui.Width - qr.width - 6f);
            qr.y = Mathf.Max(2f, qr.y);
            HudDraw.Fill(qr, new Color(0.09f, 0.07f, 0.15f, 0.72f), 8);
            if (q.Main) HudDraw.Ring(qr, new Color(1f, 0.82f, 0.4f, 0.6f), 8);
            HudDraw.Text(new Rect(qr.x + 8f, qr.y, 16f, qr.height), q.Glyph, HudStyles.SmallCenter, gc);
            HudDraw.Text(new Rect(qr.x + 24f, qr.y, qr.width - 28f, qr.height), questText, HudStyles.Small, q.Yellow ? gc : Hud.Muted);
        }

        // ---------------------------------------------------------------- exploration enemy plate (PreviewEncounter)

        Lanternvale.Session.EncounterEnemyPreview plateFor;
        IReadOnlyList<Lanternvale.Session.EncounterEnemyPreview> plateGroup;
        int plateParty = -1;
        string plateName = "", plateLevel = "", plateGroupText = "", plateMark;
        Color plateLevelCol, plateMarkCol;

        void BuildEnemyPlate(Lanternvale.Session.EncounterEnemyPreview p, IReadOnlyList<Lanternvale.Session.EncounterEnemyPreview> group, int party)
        {
            plateFor = p;
            plateGroup = group;
            plateParty = party;
            plateName = p.Name ?? "";
            int lo = p.MinLevel > 0 ? p.MinLevel : p.Level, hi = Mathf.Max(lo, p.MaxLevel > 0 ? p.MaxLevel : p.Level);
            bool boss = p.Rank == CreatureRank.Boss;
            bool elite = p.Rank == CreatureRank.Elite || p.Rank == CreatureRank.Rare;
            // WoW: a boss (or anything 10+ levels above you) shows "??" with a skull; elites add "+"
            if (boss || hi - party >= 10) plateLevel = "??";
            else plateLevel = (lo == hi ? HudText.Int(lo) : HudText.Int(lo) + "-" + HudText.Int(hi)) + (elite ? "+" : "");
            plateLevelCol = boss || hi - party >= 10 ? Hud.C("#ff4a3a") : LevelColor(hi - party);
            plateMark = boss ? "glyph_skull" : elite ? "glyph_wings" : null;
            plateMarkCol = boss ? Hud.C("#ff8a7a") : p.Rank == CreatureRank.Rare ? Hud.C("#d9e0ea") : Ui.Gold;
            plateGroupText = "";
            if (group != null && group.Count > 1)
            {
                bool same = true;
                CreatureRank top = CreatureRank.Normal;
                for (int i = 0; i < group.Count; i++)
                {
                    if (group[i].CreatureId != p.CreatureId) same = false;
                    if (Danger(group[i].Rank) > Danger(top)) top = group[i].Rank;
                }
                plateGroupText = same ? "pack of " + group.Count : "group of " + group.Count;
                if (!same && Danger(top) > Danger(p.Rank)) plateGroupText += " · " + Hud.RankName(top);
            }
            if (p.Passive) plateGroupText = plateGroupText.Length > 0 ? plateGroupText + " · passive" : "passive";
        }

        static int Danger(CreatureRank r) => r == CreatureRank.Boss ? 3 : r == CreatureRank.Elite ? 2 : r == CreatureRank.Rare ? 1 : 0;

        /// <summary>WoW level colours (target frame thresholds): red ≥ +5, orange ≥ +3, yellow ≥ -2, green ≥ -7, grey.</summary>
        static Color LevelColor(int diff)
        {
            if (diff >= 5) return Hud.C("#ff4a3a");
            if (diff >= 3) return Hud.C("#ff8a3a");
            if (diff >= -2) return Hud.C("#ffe14a");
            if (diff >= -7) return Hud.C("#5ee05e");
            return Hud.C("#9d9d9d");
        }

        void DrawEnemyPreview(Vector2 pos, GameFlow f, string prompt)
        {
            var p = f.HoveredEnemy;
            var group = f.HoveredEncounter;
            int party = f.Session != null ? f.Session.PartyLevel : 1;
            if (!ReferenceEquals(p, plateFor) || !ReferenceEquals(group, plateGroup) || party != plateParty) BuildEnemyPlate(p, group, party);
            var col = Hud.C("#ff9a88");
            float nameW = Measure(plateName, HudStyles.Label18);
            float levelW = Measure(plateLevel, HudStyles.TinyCenter) + 14f;
            float markW = plateMark != null ? 24f : 0f;
            float w = Mathf.Max(nameW + levelW + markW + 34f, plateGroupText.Length > 0 ? Measure(plateGroupText, HudStyles.Small) + 26f : 0f);
            float pw = string.IsNullOrEmpty(prompt) ? 0f : Measure(prompt, HudStyles.Small) + 34f;
            float total = Mathf.Max(w, pw);
            float h = plateGroupText.Length > 0 ? 46f : 28f;
            var r = new Rect(Mathf.Round(pos.x - total * 0.5f), Mathf.Round(pos.y - 34f - (h - 28f) - (pw > 0f ? 20f : 0f)), total, h);
            r.x = Mathf.Clamp(r.x, 6f, Ui.Width - r.width - 6f);
            r.y = Mathf.Clamp(r.y, 6f, Ui.Height - 80f);
            HudDraw.Fill(r, new Color(0.09f, 0.07f, 0.15f, 0.8f), 8);
            HudDraw.Ring(r, new Color(col.r, col.g, col.b, 0.55f), 8);
            if (plateMark != null) HudDraw.Ring(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(plateMarkCol.r, plateMarkCol.g, plateMarkCol.b, 0.5f), 8);
            // row 1: [mark] name [level]
            float x = r.x + 12f;
            float rowY = r.y;
            if (plateMark != null)
            {
                HudDraw.Glyph(new Rect(x, rowY + 5f, 18f, 18f), plateMark, plateMarkCol);
                x += markW;
            }
            HudDraw.Text(new Rect(x, rowY, nameW + 4f, 28f), plateName, HudStyles.Label18, col);
            var lb = new Rect(r.xMax - 10f - levelW, rowY + 5f, levelW, 18f);
            HudDraw.Fill(lb, new Color(0.04f, 0.03f, 0.08f, 0.9f), 5);
            HudDraw.Ring(lb, new Color(plateLevelCol.r, plateLevelCol.g, plateLevelCol.b, 0.8f), 5);
            HudDraw.Text(lb, plateLevel, HudStyles.TinyCenter, plateLevelCol, false);
            // row 2: group
            if (plateGroupText.Length > 0)
                HudDraw.Text(new Rect(r.x + 12f, rowY + 25f, r.width - 24f, 18f), plateGroupText, HudStyles.Small, Hud.Muted);
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

        int measuredVersion = -1;

        float Measure(string text, GUIStyle st)
        {
            if (measuredVersion != HudStyles.Version) { measuredVersion = HudStyles.Version; widthCache.Clear(); }
            if (widthCache.TryGetValue(text, out var w)) return w;
            if (widthCache.Count > 256) widthCache.Clear();
            measure.text = text;
            w = st.CalcSize(measure).x;
            widthCache[text] = w;
            return w;
        }
    }
}
