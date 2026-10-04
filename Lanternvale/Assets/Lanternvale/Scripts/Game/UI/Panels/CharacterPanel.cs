// Character sheet (C): party member tabs, paper doll with the 17 equipment slots around the hero art (click a slot to
// unequip; click an item in the bags to equip it on this member), primary attributes, derived stats (attack power,
// spell power, crit, hit, dodge/parry/block, armour mitigation, resistances, mana regeneration), health/resource,
// experience, and approval for companions (with their likes / dislikes; hover the name line for their bio).
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class CharacterPanel : PanelWindow
    {
        public override string Id => UiPanels.Character;
        public override int Order => 120;

        static readonly EquipSlot[] LeftSlots = { EquipSlot.Head, EquipSlot.Neck, EquipSlot.Shoulder, EquipSlot.Back, EquipSlot.Chest, EquipSlot.Wrist, EquipSlot.Hands };
        static readonly EquipSlot[] RightSlots = { EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Finger1, EquipSlot.Finger2, EquipSlot.Trinket1, EquipSlot.Trinket2 };
        static readonly EquipSlot[] BottomSlots = { EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ranged };
        static readonly School[] MagicSchools = { School.Holy, School.Fire, School.Nature, School.Frost, School.Shadow, School.Arcane };
        static readonly School[] ResistSchools = { School.Fire, School.Frost, School.Nature, School.Shadow, School.Arcane };

        struct StatLine
        {
            public string Label, Value, Tip;
            public bool Header;
        }

        readonly List<StatLine> stats = new List<StatLine>();
        Unit statsFor;
        float statsAt;
        string titleLine = "", xpText = "", approvalText = "", healthText = "", resourceText = "", petLine = "";
        float xpFill, approvalFill;
        int approval;
        int lockFrame;

        protected override void DrawPanel()
        {
            var s = PanelKit.Sess;
            var u = PanelKit.Member;
            var r = PanelKit.Fit(new Rect(40f, 66f, 900f, Mathf.Min(890f, Ui.Height - 160f)));
            var c = Chrome(r, "Character");
            var nm = PanelKit.MemberTabs(new Rect(c.x, c.y - 4f, c.width, 52f), u, s.Roster.Count > s.Party.Count ? s.Roster : s.Party, 48f);
            if (nm != u) { PanelKit.Member = nm; u = nm; statsAt = 0f; }
            if (u == null) return;
            if (u != statsFor || Time.unscaledTime >= statsAt) Rebuild(s, u);

            float y = c.y + 56f;
            PanelKit.Label(new Rect(c.x, y, c.width, 30f), titleLine, PanelKit.Text);
            // companions: hovering the name line tells who they are (bio, personality, likes and dislikes)
            if (u.Companion != null) Ui.TooltipFor(new Rect(c.x, y, c.width, 30f), CompanionAbout(u.Companion));
            y += 34f;
            // bars
            float bw = 380f;
            Ui.Bar(new Rect(c.x, y, bw, 22f), u.MaxHealth > 0 ? u.Health / u.MaxHealth : 0f, Ui.Health, healthText);
            if (u.PowerType != ResourceType.None)
            {
                float max = u.MaxResource(u.PowerType);
                Ui.Bar(new Rect(c.x + bw + 12f, y, bw, 22f), max > 0f ? u.GetResource(u.PowerType) / max : 0f, Ui.ResourceColor(u.PowerType), resourceText);
            }
            y += 34f;

            // paper doll
            const float slot = 58f, sgap = 8f;
            var doll = new Rect(c.x, y, 400f, LeftSlots.Length * (slot + sgap) + slot + 14f);
            DrawDoll(doll, u, s, slot, sgap);
            // stats
            var sr = new Rect(doll.xMax + 26f, y, c.xMax - doll.xMax - 26f, doll.height);
            DrawStats(sr);
            y = doll.yMax + 10f;
            // experience & approval
            if (u.IsMainCharacter || u == s.Main)
            {
                PanelKit.Label(new Rect(c.x, y, 160f, 26f), "Experience", PanelKit.TextBoldSmall);
                Ui.Bar(new Rect(c.x + 130f, y + 2f, c.width - 130f, 22f), xpFill, Ui.Hex("#b07be0"), xpText);
                y += 32f;
            }
            else if (u.Companion != null)
            {
                PanelKit.Label(new Rect(c.x, y, 160f, 26f), "Approval", PanelKit.TextBoldSmall);
                var bar = new Rect(c.x + 130f, y + 2f, c.width - 130f, 22f);
                Ui.Bar(bar, approvalFill, approval >= 0 ? Ui.Hex("#f2a6c2") : Ui.Hex("#8f8aa6"), approvalText);
                PanelKit.Rect(new Rect(bar.center.x - 1f, bar.y - 2f, 2f, bar.height + 4f), new Color(0.17f, 0.13f, 0.22f, 0.5f));
                var ct = CompanionTextOf(u.Companion);
                Ui.TooltipFor(new Rect(c.x, y, c.width, 26f), ct.ApprovalTip);
                y += 32f;
                // what earns (and costs) their approval: the authored likes / dislikes, full lists in the tooltip
                if (ct.Likes.Length > 0 && y < c.yMax - 24f)
                {
                    TasteLine(new Rect(c.x, y, c.width, 24f), "Likes", ct.Likes, PanelKit.GoodDark, ct.ApprovalTip);
                    y += 26f;
                }
                if (ct.Dislikes.Length > 0 && y < c.yMax - 24f)
                {
                    TasteLine(new Rect(c.x, y, c.width, 24f), "Dislikes", ct.Dislikes, PanelKit.BadDark, ct.ApprovalTip);
                    y += 26f;
                }
            }
            if (u.Pet != null && y < c.yMax - 26f)
                PanelKit.Label(new Rect(c.x, y, c.width, 26f), petLine, PanelKit.TextSmall);
        }

        void DrawDoll(Rect r, Unit u, GameSession s, float slot, float gap)
        {
            float artX = r.x + slot + 16f, artW = r.width - 2f * (slot + 16f);
            var art = new Rect(artX, r.y, artW, LeftSlots.Length * (slot + gap) - gap);
            PanelKit.Rounded(art, new Color(0.17f, 0.13f, 0.22f, 0.07f));
            var cc = PanelKit.ColorOf(u);
            PanelKit.Tex(new Rect(art.x + 10f, art.y + 40f, art.width - 20f, art.height - 80f), ProceduralArt.Glow, new Color(cc.r, cc.g, cc.b, 0.4f));
            var tex = ArtLibrary.Texture(PanelKit.SpriteOf(u));
            PanelKit.Tex(new Rect(art.x + 8f, art.y + 12f, art.width - 16f, art.height - 24f), tex, Color.white, ScaleMode.ScaleToFit);
            for (int i = 0; i < LeftSlots.Length; i++)
                DrawSlot(new Rect(r.x, r.y + i * (slot + gap), slot, slot), u, LeftSlots[i]);
            for (int i = 0; i < RightSlots.Length; i++)
                DrawSlot(new Rect(r.xMax - slot, r.y + i * (slot + gap), slot, slot), u, RightSlots[i]);
            float bx = art.center.x - (BottomSlots.Length * (slot + gap) - gap) * 0.5f;
            float by = art.yMax + 12f;
            for (int i = 0; i < BottomSlots.Length; i++)
                DrawSlot(new Rect(bx + i * (slot + gap), by, slot, slot), u, BottomSlots[i]);
        }

        void DrawSlot(Rect r, Unit u, EquipSlot slot)
        {
            var it = u.Equipment[slot];
            bool hover = PanelKit.Hover(r);
            if (it == null)
            {
                PanelKit.EmptySlot(r, PanelKit.SlotLabel(slot));
                if (hover) Ui.TooltipFor(r, SlotTip(slot));
            }
            else
            {
                PanelKit.ItemIcon(r, it, false, hover);
                if (hover) Ui.TooltipFor(r, PanelKit.ItemTip(it, u, false, Ui.Rich("Click to unequip", Ui.TextMuted)));
            }
            GameInput.BlockRectGui(r);
            if (it != null && PanelKit.Click(r, out _) && Time.frameCount > lockFrame)
            {
                lockFrame = Time.frameCount + 1;
                var who = u;
                var sl = slot;
                PanelKit.Do(() =>
                {
                    var ss = PanelKit.Sess;
                    if (ss != null && PanelKit.Try(() => ss.Unequip(who, sl))) Ui.Sfx?.Invoke("ui_close");
                });
            }
        }

        static readonly Dictionary<EquipSlot, string> slotTips = new Dictionary<EquipSlot, string>();

        static string SlotTip(EquipSlot s)
        {
            if (slotTips.TryGetValue(s, out var t)) return t;
            t = $"<b>{UiText.Spaced(s.ToString()).Replace("1", "").Replace("2", "").Trim()}</b>\n" + Ui.Rich("Empty — click an item in your bags (I) to equip it.", Ui.TextMuted);
            slotTips[s] = t;
            return t;
        }

        void DrawStats(Rect r)
        {
            const float lineH = 25f;
            float colW = (r.width - 18f) * 0.5f;
            int perCol = Mathf.Max(1, Mathf.FloorToInt(r.height / lineH));
            // split at a header near the middle so sections are not cut
            int split = Mathf.Min(stats.Count, perCol);
            int mid = stats.Count / 2;
            for (int i = mid; i < stats.Count && i < perCol; i++) if (stats[i].Header) { split = i; break; }
            for (int i = 0; i < stats.Count; i++)
            {
                int col = i < split ? 0 : 1;
                int row = col == 0 ? i : i - split;
                var lr = new Rect(r.x + col * (colW + 18f), r.y + row * lineH, colW, lineH);
                if (lr.yMax > r.yMax + 2f) continue;
                var l = stats[i];
                if (l.Header)
                {
                    PanelKit.Label(new Rect(lr.x, lr.y + 2f, lr.width, lineH), l.Label, PanelKit.TextBold, PanelKit.GoldInk);
                    continue;
                }
                if (row % 2 == 1) PanelKit.Rect(lr, new Color(0.55f, 0.42f, 0.25f, 0.06f));
                PanelKit.Label(new Rect(lr.x + 6f, lr.y + 2f, lr.width * 0.62f, lineH), l.Label, PanelKit.TextSmall);
                PanelKit.Label(new Rect(lr.x, lr.y, lr.width - 6f, lineH), l.Value, PanelKit.TextSmallRight);
                if (!string.IsNullOrEmpty(l.Tip)) Ui.TooltipFor(lr, l.Tip);
            }
        }

        // ------------------------------------------------------------------ stat text (rebuilt 4×/s)

        void Rebuild(GameSession s, Unit u)
        {
            statsFor = u;
            statsAt = Time.unscaledTime + 0.25f;
            stats.Clear();
            var st = u.Stats;
            var db = PanelKit.Db;
            string role = u.Role != UnitRole.Auto ? "  ·  " + RoleName(u.Role) : "";
            titleLine = $"<b>{PanelKit.NameOf(u)}</b>   Level {u.Level} {Ui.Rich(u.Class != null ? u.Class.name : "", u.Class != null ? PanelKit.InkColorOf(u.Class.id) : Ui.Ink)}{role}";
            healthText = $"{Mathf.CeilToInt(u.Health)} / {Mathf.CeilToInt(u.MaxHealth)}";
            resourceText = u.PowerType == ResourceType.None ? "" :
                $"{Mathf.FloorToInt(u.GetResource(u.PowerType))} / {Mathf.RoundToInt(u.MaxResource(u.PowerType))} {PanelKit.ResourceName(u.PowerType)}";

            Header("Attributes");
            Primary("Strength", st.Strength, st.BaseStrength, "Attack power for warriors, paladins and shamans; block value.");
            Primary("Agility", st.Agility, st.BaseAgility, "Critical strikes, dodge and armour; attack power for rogues and hunters.");
            Primary("Stamina", st.Stamina, st.BaseStamina, "Health: 10 per point above 20.");
            Primary("Intellect", st.Intellect, st.BaseIntellect, "Mana (15 per point above 20) and spell critical strikes.");
            Primary("Spirit", st.Spirit, st.BaseSpirit, "Health and mana regeneration out of the five-second rule.");

            int L = Mathf.Max(1, u.Level);
            float mit = Mathf.Min(75f, st.Armor / (st.Armor + 400f + 85f * L) * 100f);
            Header("Defence");
            Line("Armour", $"{Mathf.RoundToInt(st.Armor)}  ({PanelKit.Pct(mit)})", $"Reduces physical damage from a level {L} attacker by {PanelKit.Pct(mit)}.");
            Line("Dodge", PanelKit.Pct(st.Dodge));
            if (st.CanParry) Line("Parry", PanelKit.Pct(st.Parry), "Only against attacks from the front.");
            if (st.CanBlock || u.Equipment.HasShield) Line("Block", $"{PanelKit.Pct(st.BlockChance)}  ({Mathf.RoundToInt(st.BlockValue)})", "Chance to block frontal attacks with a shield and the damage blocked.");
            var resist = new StringBuilder();
            foreach (var sc in ResistSchools)
            {
                float v = st.Resistance(sc);
                if (resist.Length > 0) resist.Append("  ");
                resist.Append(Ui.Rich(Mathf.RoundToInt(v).ToString(), Color.Lerp(Ui.SchoolColor(sc), Ui.Ink, 0.45f)));
            }
            Line("Resistances", resist.ToString(), "Fire, Frost, Nature, Shadow, Arcane. Average mitigation = resistance / (5 × attacker level) × 75%.");

            Header("Melee");
            var mh = StatCalculator.GetWeapon(u, WeaponSlot.MainHand);
            if (mh.Valid)
            {
                float bonus = st.AttackPower / 14f * mh.Speed;
                Line("Damage", $"{Mathf.RoundToInt(mh.Min + bonus)}–{Mathf.RoundToInt(mh.Max + bonus)}", $"Main hand, {mh.Speed:0.0} s per swing (one swing every {mh.Speed / Mathf.Max(0.1f, st.MeleeHaste):0.0} s).");
            }
            Line("Attack power", Mathf.RoundToInt(st.AttackPower).ToString(), "Adds AP / 14 × weapon speed to each swing.");
            Line("Crit", PanelKit.Pct(st.MeleeCrit));
            Line("Hit", "+" + PanelKit.Pct(st.MeleeHit), "Reduces your chance to miss (5% base, more against higher levels).");
            bool ranged = u.ClassId == ClassId.Hunter || u.Equipment.HasRangedWeapon && u.ClassId != ClassId.Mage && u.ClassId != ClassId.Priest && u.ClassId != ClassId.Warlock;
            if (ranged)
            {
                Header("Ranged");
                Line("Attack power", Mathf.RoundToInt(st.RangedAttackPower).ToString());
                Line("Crit", PanelKit.Pct(st.RangedCrit));
                Line("Hit", "+" + PanelKit.Pct(st.RangedHit));
            }
            if (u.PowerType == ResourceType.Mana || st.HealingPower > 0f)
            {
                Header("Spells");
                float sp = 0f, crit = 0f, hit = 0f;
                foreach (var sc in MagicSchools)
                {
                    sp = Mathf.Max(sp, st.SpellDamage(sc));
                    crit = Mathf.Max(crit, st.SpellCrit(sc));
                    hit = Mathf.Max(hit, st.SpellHit(sc));
                }
                Line("Spell power", Mathf.RoundToInt(sp).ToString(), "Bonus spell damage (highest school).");
                Line("Healing", Mathf.RoundToInt(st.HealingPower).ToString());
                Line("Crit", PanelKit.Pct(crit));
                Line("Hit", "+" + PanelKit.Pct(hit));
                if (u.PowerType == ResourceType.Mana)
                {
                    float spirit5 = st.SpiritRegenPerTick * 2.5f;
                    float casting = st.ManaRegen + spirit5 * Mathf.Clamp01(st.SpiritRegenWhileCasting / 100f);
                    Line("Mana regen", $"{Mathf.RoundToInt(spirit5 + st.ManaRegen)} / 5 s", $"While casting (five-second rule): {Mathf.RoundToInt(casting)} per 5 s.\nMP5 from items: {Mathf.RoundToInt(st.ManaRegen)}.");
                }
            }

            // experience / approval
            if (db != null && u.Level < Mathf.Max(1, db.Config.maxLevel))
            {
                int need = 0;
                try { need = Progression.XpToNextLevel(db, u.Level); } catch (Exception) { }
                xpFill = need > 0 ? Mathf.Clamp01(u.Xp / (float)need) : 0f;
                xpText = need > 0 ? $"{u.Xp} / {need} XP" : "";
            }
            else { xpFill = 1f; xpText = "Maximum level"; }
            petLine = u.Pet != null ? $"{(u.Pet.Creature != null && u.Pet.Creature.type == CreatureType.Demon ? "Demon" : "Pet")}: <b>{PanelKit.NameOf(u.Pet)}</b> (level {u.Pet.Level})" : "";
            if (u.Companion != null)
            {
                approval = s.GetApproval(u.Companion.id);
                approvalFill = Mathf.Clamp01((approval + 100f) / 200f);
                approvalText = $"{approval:+0;-0;0}  ·  {ApprovalWord(approval)}";
            }
        }

        static string RoleName(UnitRole r)
        {
            switch (r)
            {
                case UnitRole.Tank: return "Tank";
                case UnitRole.Healer: return "Healer";
                case UnitRole.MeleeDps: return "Melee damage";
                case UnitRole.RangedDps: return "Ranged damage";
                default: return "";
            }
        }

        public static string ApprovalWord(int a)
        {
            if (a >= 60) return "Devoted";
            if (a >= 30) return "Fond of you";
            if (a >= 10) return "Approves";
            if (a > -10) return "Neutral";
            if (a > -30) return "Wary";
            return "Disapproves";
        }

        static void TasteLine(Rect r, string label, string list, Color labelColor, string tip)
        {
            PanelKit.Label(new Rect(r.x, r.y, 130f, r.height), label, PanelKit.TextBoldSmall, labelColor);
            PanelKit.Label(new Rect(r.x + 130f, r.y, r.width - 130f, r.height), list, PanelKit.RowTextSmall);
            Ui.TooltipFor(r, tip);
        }

        // ------------------------------------------------------------------ companion bio (companions.json; cached per def)

        sealed class CompanionText
        {
            public string About = "", ApprovalTip = "", Likes = "", Dislikes = "";
        }

        static readonly Dictionary<CompanionDef, CompanionText> companionTexts = new Dictionary<CompanionDef, CompanionText>();

        static CompanionText CompanionTextOf(CompanionDef c)
        {
            if (companionTexts.TryGetValue(c, out var t)) return t;
            t = new CompanionText { Likes = JoinList(c.likes), Dislikes = JoinList(c.dislikes) };
            string name = string.IsNullOrEmpty(c.name) ? c.id : c.name;
            string tastes = "";
            if (t.Likes.Length > 0) tastes = Ui.Rich("Likes: ", Ui.Good) + t.Likes;
            if (t.Dislikes.Length > 0) tastes += (tastes.Length > 0 ? "\n" : "") + Ui.Rich("Dislikes: ", Ui.Bad) + t.Dislikes;

            var sb = new StringBuilder();
            sb.Append("<b>").Append(name).Append("</b>");
            if (!string.IsNullOrEmpty(c.title)) sb.Append("  ·  ").Append(Ui.Rich(c.title, Ui.Gold));
            if (!string.IsNullOrEmpty(c.bio)) sb.Append('\n').Append(c.bio);
            if (!string.IsNullOrEmpty(c.personality)) sb.Append("\n\n").Append(Ui.Rich("<i>" + c.personality + "</i>", Ui.TextMuted));
            if (tastes.Length > 0) sb.Append("\n\n").Append(tastes);
            t.About = sb.ToString();

            t.ApprovalTip = $"<b>Approval</b>\n{name}'s opinion of you rises with choices they like and falls with ones they dislike."
                            + (tastes.Length > 0 ? "\n\n" + tastes : "");
            companionTexts[c] = t;
            return t;
        }

        /// <summary>Companion tooltip: name and title, bio, personality, likes and dislikes (approval hints).</summary>
        public static string CompanionAbout(CompanionDef c) => c != null ? CompanionTextOf(c).About : null;

        static string JoinList(string[] items)
        {
            if (items == null || items.Length == 0) return "";
            var sb = new StringBuilder();
            foreach (var it in items)
            {
                if (string.IsNullOrEmpty(it)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(it);
            }
            return sb.ToString();
        }

        void Header(string label) => stats.Add(new StatLine { Label = label, Header = true });
        void Line(string label, string value, string tip = null) => stats.Add(new StatLine { Label = label, Value = value, Tip = tip });

        void Primary(string label, float total, float baseValue, string tip)
        {
            int t = Mathf.RoundToInt(total), b = Mathf.RoundToInt(baseValue);
            string v = t > b ? Ui.Rich(t.ToString(), PanelKit.GoodDark) : t < b ? Ui.Rich(t.ToString(), PanelKit.BadDark) : t.ToString();
            string extra = t != b ? $"\nBase {b}, {(t > b ? "+" : "")}{t - b} from gear and effects." : "";
            Line(label, v, tip + extra);
        }
    }
}
