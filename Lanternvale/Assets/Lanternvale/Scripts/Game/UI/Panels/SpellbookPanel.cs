// Spellbook (P): the selected member's known abilities, grouped — general (attacks), class abilities by school,
// talent abilities, the pet's abilities, then passives. Each entry: icon, name, "Rank 3 (next at 24)", cost and cast
// time; tooltips with the real numbers (UiText.Ability). Click to use: out of combat through the field
// (UseAbilityOutOfCombat; enemy abilities arm an opener), in combat on the member's own turn (Combat.BeginAbility).
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class SpellbookPanel : PanelWindow
    {
        public override string Id => UiPanels.Spellbook;
        public override int Order => 122;

        sealed class Entry
        {
            public AbilityDef Ability;
            public Unit Owner;
            public string Name = "", Sub = "", Tip;
            public int TipStamp = -1;
        }

        sealed class Group
        {
            public string Title = "";
            public Color Color;
            public readonly List<Entry> Entries = new List<Entry>();
        }

        readonly List<Group> groups = new List<Group>();
        readonly PanelKit.ScrollState scroll = new PanelKit.ScrollState();
        Unit builtFor;
        int builtStamp = -1;
        int lockFrame;
        Unit headFor;
        int headLevel;
        string headText = "";

        static readonly School[] SchoolOrder = { School.Physical, School.Holy, School.Fire, School.Frost, School.Arcane, School.Nature, School.Shadow };

        static int StampOf(Unit u)
        {
            int st = u.Id * 977 + u.Level * 31;
            foreach (var kv in u.Abilities) st += kv.Value * 7 + kv.Key.Length;
            if (u.Pet != null) { st += u.Pet.Id * 13; foreach (var kv in u.Pet.Abilities) st += kv.Value + kv.Key.Length * 3; }
            return st;
        }

        void Build(Unit u)
        {
            builtFor = u;
            builtStamp = StampOf(u);
            groups.Clear();
            var db = PanelKit.Db;
            if (db == null) return;
            var general = new Group { Title = "General", Color = Ui.InkSoft };
            var talents = new Group { Title = "From talents", Color = Ui.GoldDeep };
            var passives = new Group { Title = "Passive", Color = Ui.InkSoft };
            var bySchool = new Dictionary<School, Group>();
            foreach (var kv in u.Abilities)
            {
                var a = db.Ability(kv.Key);
                if (a == null || a.hidden) continue;
                var e = new Entry { Ability = a, Owner = u, Name = a.name };
                if (a.passive) passives.Entries.Add(e);
                else if (a.fromTalent) talents.Entries.Add(e);
                else if (a.classId == ClassId.None || a.autoAttack) general.Entries.Add(e);
                else
                {
                    if (!bySchool.TryGetValue(a.school, out var g))
                    {
                        g = new Group { Title = SchoolTitle(a.school, u), Color = Ui.SchoolColor(a.school) };
                        bySchool[a.school] = g;
                    }
                    g.Entries.Add(e);
                }
            }
            foreach (var sc in SchoolOrder) if (bySchool.TryGetValue(sc, out var g)) groups.Add(g);
            if (talents.Entries.Count > 0) groups.Add(talents);
            if (u.Pet != null && u.Pet.Abilities.Count > 0)
            {
                var pet = new Group { Title = "Pet · " + PanelKit.NameOf(u.Pet), Color = Ui.Focus };
                foreach (var kv in u.Pet.Abilities)
                {
                    var a = db.Ability(kv.Key);
                    if (a == null || a.hidden || a.autoAttack) continue;
                    pet.Entries.Add(new Entry { Ability = a, Owner = u.Pet, Name = a.name });
                }
                if (pet.Entries.Count > 0) groups.Add(pet);
            }
            if (general.Entries.Count > 0) groups.Add(general);
            if (passives.Entries.Count > 0) groups.Add(passives);
            foreach (var g in groups)
            {
                g.Entries.Sort((x, y) => x.Ability.learnLevel != y.Ability.learnLevel ? x.Ability.learnLevel.CompareTo(y.Ability.learnLevel) : string.CompareOrdinal(x.Name, y.Name));
                foreach (var e in g.Entries) e.Sub = SubOf(e);
            }
        }

        static string SchoolTitle(School s, Unit u)
        {
            if (s != School.Physical) return s.ToString();
            switch (u.ClassId)
            {
                case ClassId.Warrior: return "Arms & tactics";
                case ClassId.Rogue: return "Combat & guile";
                case ClassId.Hunter: return "Marksmanship & beasts";
                default: return "Physical";
            }
        }

        static string SubOf(Entry e)
        {
            var a = e.Ability;
            var u = e.Owner;
            var sb = new StringBuilder();
            int ranks = AbilityRules.RankCount(a);
            int rank = u.RankOf(a.id);
            if (ranks > 1)
            {
                sb.Append("Rank ").Append(rank);
                if (rank < ranks)
                {
                    int next = AbilityRules.RankLevel(a, rank + 1);
                    sb.Append(" (next at ").Append(next).Append(')');
                }
                else sb.Append(" (max)");
            }
            if (a.passive) return sb.Length > 0 ? sb.ToString() : "Passive";
            try
            {
                var mods = AbilityMods.For(u, a);
                float cost = a.cost != null && a.cost.type != ResourceType.None ? AbilityRules.ResourceCost(u, a, Math.Max(1, rank), mods) : 0f;
                if (cost > 0f) { if (sb.Length > 0) sb.Append("  ·  "); sb.Append(Mathf.RoundToInt(cost)).Append(' ').Append(PanelKit.ResourceName(a.cost.type)); }
                float cast = AbilityRules.CastTime(u, a, mods);
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(a.channeled ? $"{cast:0.#} s channel" : cast > 0f ? $"{cast:0.#} s cast" : "Instant");
                float cd = AbilityRules.Cooldown(u, a, mods);
                if (cd > 0f) sb.Append("  ·  ").Append(UiText.Duration(cd)).Append(" cd");
            }
            catch (Exception) { }
            return sb.ToString();
        }

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            var u = PanelKit.Member;
            float w = 760f;
            var r = PanelKit.Fit(new Rect(Mathf.Max(40f, Ui.Width * 0.5f - w - 30f), 66f, w, Mathf.Min(900f, Ui.Height - 160f)));
            var c = Chrome(r, "Spellbook");
            var nm = PanelKit.MemberTabs(new Rect(c.x, c.y - 4f, c.width, 48f), u, PanelKit.Characters, 46f);
            if (nm != u) { PanelKit.Member = nm; u = nm; scroll.Reset(); }
            if (u == null) return;
            if (u != builtFor || Time.frameCount % 20 == 0 && StampOf(u) != builtStamp) Build(u);
            if (headFor != u || headLevel != u.Level)
            {
                headFor = u;
                headLevel = u.Level;
                headText = $"<b>{PanelKit.NameOf(u)}</b> · level {u.Level} {(u.Class != null ? u.Class.name : "")}";
            }
            PanelKit.Label(new Rect(c.x + PanelKit.Characters.Count * 56f + 10f, c.y + 2f, c.width - PanelKit.Characters.Count * 56f - 10f, 40f), headText, PanelKit.Text);
            var area = new Rect(c.x, c.y + 56f, c.width, c.height - 92f);
            const float rowH = 64f, headH = 40f, colGap = 14f;
            float colW = (area.width - 20f - colGap) * 0.5f;
            float content = 0f;
            foreach (var g in groups) content += headH + Mathf.CeilToInt(g.Entries.Count / 2f) * rowH + 8f;
            float cw = PanelKit.BeginScroll(area, scroll, content);
            colW = (cw - colGap) * 0.5f;
            try
            {
                float y = 0f;
                foreach (var g in groups)
                {
                    var hr = new Rect(0f, y, cw, headH);
                    if (scroll.IsVisible(hr))
                    {
                        PanelKit.Rect(new Rect(0f, y + headH - 6f, cw, 2f), new Color(g.Color.r, g.Color.g, g.Color.b, 0.5f));
                        PanelKit.Label(new Rect(0f, y + 2f, cw, 32f), g.Title, PanelKit.Heading);
                    }
                    y += headH;
                    for (int i = 0; i < g.Entries.Count; i++)
                    {
                        var e = g.Entries[i];
                        var er = new Rect((i % 2) * (colW + colGap), y + (i / 2) * rowH, colW, rowH - 6f);
                        if (scroll.IsVisible(er)) DrawEntry(er, e, s);
                    }
                    y += Mathf.CeilToInt(g.Entries.Count / 2f) * rowH + 8f;
                }
            }
            finally { PanelKit.EndScroll(scroll); }
            if (groups.Count == 0) PanelKit.Label(area, "No abilities yet.", PanelKit.TextCenter);
            PanelKit.Label(new Rect(c.x, c.yMax - 30f, c.width, 28f), PanelKit.InCombat ? "Click an ability to use it on this member's turn." : "Click an ability to use it now (buffs, heals, summons, openers).", PanelKit.TextMutedSmall);
        }

        void DrawEntry(Rect er, Entry e, GameSession s)
        {
            var a = e.Ability;
            bool hover = PanelKit.Hover(er);
            PanelKit.Rounded(er, hover && !a.passive ? PanelKit.RowHover : PanelKit.RowShade);
            float cdLeft = 0f, cdTotal = 0f;
            try
            {
                cdLeft = e.Owner.CooldownLeft(a);
                cdTotal = Mathf.Max(a.cooldown, cdLeft);
            }
            catch (Exception) { }
            var ir = new Rect(er.x + 5f, er.y + 5f, 48f, 48f);
            Ui.Icon(ir, a.icon, Ui.SchoolColor(a.school), cdTotal > 0f ? cdLeft / cdTotal : 0f, a.passive);
            PanelKit.Label(new Rect(er.x + 62f, er.y + 6f, er.width - 68f, 26f), e.Name, PanelKit.RowText);
            PanelKit.Label(new Rect(er.x + 62f, er.y + 31f, er.width - 68f, 22f), e.Sub, PanelKit.RowTextSmall);
            if (hover) Ui.TooltipFor(er, TipOf(e));
            if (!a.passive && PanelKit.LeftClick(er) && Time.frameCount > lockFrame)
            {
                lockFrame = Time.frameCount + 1;
                var owner = e.Owner;
                string id = a.id;
                PanelKit.Do(() => UseAbility(owner, id));
            }
        }

        string TipOf(Entry e)
        {
            int st = e.Owner.RankOf(e.Ability.id) * 100 + e.Owner.Level;
            if (e.Tip != null && e.TipStamp == st) return e.Tip;
            e.TipStamp = st;
            e.Tip = UiText.Ability(e.Owner, e.Ability);
            return e.Tip;
        }

        static void UseAbility(Unit u, string id)
        {
            var f = PanelKit.Flow;
            var s = PanelKit.Sess;
            if (f == null || s == null || u == null) return;
            if (s.Mode == SessionMode.Combat)
            {
                var c = f.Combat;
                if (c == null) return;
                if (c.ActiveUnit != u) { PanelKit.Notice("Wait for " + PanelKit.NameOf(u) + "'s turn."); return; }
                PanelKit.Try(() => c.BeginAbility(id));
                return;
            }
            if (s.Mode != SessionMode.Exploration) return;
            PanelKit.Try(() => f.UseAbilityOutOfCombat(u, id));
        }
    }
}
