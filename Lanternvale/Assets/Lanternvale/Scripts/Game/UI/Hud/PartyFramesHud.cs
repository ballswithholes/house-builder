// Party frames (left column): portrait with class-colour frame, name, level badge, health (+absorb) and
// resource bars, combo-point pips, pending-cast bar, XP (main character), buffs/debuffs with time & stacks,
// pet sub-frames, totems, downed/dead states, auto-play toggle (companions) and the leader's crown.
// Click selects (GameFlow.Select); in the out-of-combat "choose a party member" mode the click casts instead.
// Right-click one of your own buffs to cancel it (Ice Block, stealth, aspects…). Warlocks show their Soul Shards.
// In combat health/death are read as presented (HudPresented), so bars move when the blow lands on screen.
// The column never skips a member: when the party does not fit above the bottom HUD block (larger interface sizes)
// the extras give way first — fewer aura rows, pet auras, buffs (debuffs stay), pet/totem rows (see Layouts).
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed class PartyFramesHud : IUiScreen
    {
        public string Id => "";
        public int Order => Hud.OrderPartyFrames;
        public bool Visible => Hud.WorldHud && !(Hud.Session?.InRaid ?? false);   // a raid: RaidFramesHud
        public bool Modal => false;

        const float X = 14f, Y = 14f, W = 300f, H = 90f, Portrait = 70f;
        const float PetW = 236f, PetH = 38f, PetIndent = 44f;
        const float AuraSize = 24f, AuraGap = 3f, TotemSize = 24f;
        const float DeadPetHintH = 18f;

        /// <summary>How much of each member's extras (aura rows, pet/summon frames, totems) the column shows.</summary>
        struct FrameLayout
        {
            public readonly int AuraRows, PetAuraRows;
            public readonly bool Buffs;  // false: only debuffs (what a dispel or a heal answers) get an aura row
            public readonly bool Subs;   // pet / summon frames, the dead-pet hint and totems
            public readonly float Gap;
            public FrameLayout(int auraRows, int petAuraRows, bool buffs, bool subs, float gap)
            { AuraRows = auraRows; PetAuraRows = petAuraRows; Buffs = buffs; Subs = subs; Gap = gap; }
        }

        /// <summary>Tried in order; the first under which the whole party fits above the bottom HUD block is drawn (the
        /// last one when none does, and a member that would still push the rest past the bottom is drawn bare). Every
        /// member is always drawn: at larger interface sizes the extras give way first — fewer aura rows (debuffs come
        /// first), no pet auras, no buffs, no pet/totem rows.</summary>
        static readonly FrameLayout[] Layouts =
        {
            new FrameLayout(2, 2, true, true, 10f),
            new FrameLayout(1, 1, true, true, 10f),
            new FrameLayout(1, 0, true, true, 6f),
            new FrameLayout(1, 0, false, true, 6f),
            new FrameLayout(1, 0, false, false, 4f),
        };

        static readonly Color FrameDead = new Color(0.55f, 0.52f, 0.6f, 1f);
        static readonly Color AutoOn = Ui.Hex("#7fd47a");
        static readonly Color ShardCol = Ui.Hex("#c9a6ff");
        readonly HudText.One shardsText = new HudText.One("", " Soul Shards");

        readonly List<Unit> chars = new List<Unit>(6);
        readonly List<Unit> subs = new List<Unit>(4);
        readonly List<AuraInstance> auras = new List<AuraInstance>(24);
        readonly Dictionary<string, string> deadPetText = new Dictionary<string, string>();

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

                // never skip a member: pick the fullest layout that keeps the column above the bottom HUD block
                // (the compact combat log, and the action bar row under it)
                var combat = Hud.Combat;
                var battle = combat != null ? combat.Battle : null;
                float bottom = HudLayout.CompactLog.y - 8f;
                var lay = ChooseLayout(bottom - Y, battle);
                var bare = new FrameLayout(0, 0, false, false, lay.Gap);
                float y = Y;
                for (int i = 0; i < chars.Count; i++)
                {
                    if (i > 0) y += lay.Gap;
                    // last resort (nothing fits): a member whose extras would push the rest past the bottom is drawn bare
                    float rest = (chars.Count - 1 - i) * (lay.Gap + BareHeight);
                    var li = y + CharacterHeight(chars[i], battle, lay) + rest > bottom + 0.5f ? bare : lay;
                    y = DrawCharacter(chars[i], y, s, li);
                }
            }
            catch (Exception e) { Hud.LogOnce("party:" + e.GetType().Name, "Party frames: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        // ================================================================ layout (measured with DrawCharacter's own metrics)

        FrameLayout ChooseLayout(float available, Battle battle)
        {
            for (int i = 0; i < Layouts.Length - 1; i++)
                if (PartyHeight(Layouts[i], battle) <= available) return Layouts[i];
            return Layouts[Layouts.Length - 1];
        }

        float PartyHeight(FrameLayout lay, Battle battle)
        {
            float h = 0f;
            for (int i = 0; i < chars.Count; i++)
            {
                if (i > 0) h += lay.Gap;
                h += CharacterHeight(chars[i], battle, lay);
            }
            return h;
        }

        /// <summary>A member frame without extras (DrawCharacter: the frame and the 3 px under it).</summary>
        const float BareHeight = H + 3f;

        /// <summary>The height DrawCharacter takes for u under lay (frame, aura rows, pet frames, dead-pet hint, totems).</summary>
        float CharacterHeight(Unit u, Battle battle, FrameLayout lay)
        {
            float h = BareHeight + AuraBlockHeight(CountAuras(u, lay.Buffs), W - 4f, lay.AuraRows);
            if (!lay.Subs) return h;
            CollectSubs(u, battle);
            for (int i = 0; i < subs.Count; i++)
                h += 2f + PetH + 2f + AuraBlockHeight(CountAuras(subs[i], lay.Buffs), PetW - 4f, lay.PetAuraRows) + 1f;
            if (ShowDeadPetHint(u)) h += 2f + DeadPetHintH;
            if (u.Totems.Count > 0) h += 3f + (LiveTotems(u) > 0 ? TotemSize + 2f : 0f);
            return h;
        }

        /// <summary>Auras DrawAuras would list for u (debuffs, plus buffs when buffs is set).</summary>
        static int CountAuras(Unit u, bool buffs)
        {
            int n = 0;
            var list = u.Auras;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a != null && a.Def != null && !a.Def.hidden && !a.IsPassive && (buffs || a.IsDebuff)) n++;
            }
            return n;
        }

        static int AurasPerRow(float width) => Mathf.Max(1, (int)((width + AuraGap) / (AuraSize + AuraGap)));

        static float AuraBlockHeight(int count, float width, int maxRows)
        {
            if (count <= 0 || maxRows <= 0) return 0f;
            int perRow = AurasPerRow(width);
            int rows = Mathf.Min(maxRows, (count + perRow - 1) / perRow);
            return rows * (AuraSize + AuraGap + 1f);
        }

        /// <summary>A hunter whose pet is dead and who has no other pet/summon frame shows "… is dead — Revive Pet"
        /// (call after CollectSubs).</summary>
        bool ShowDeadPetHint(Unit u) => subs.Count == 0 && u.HunterPet != null && u.HunterPet.Dead && u.Pet == null;

        static int LiveTotems(Unit u)
        {
            int n = 0;
            foreach (var kv in u.Totems) if (kv.Value != null && !kv.Value.Dead) n++;
            return n;
        }

        // ================================================================ character frame

        float DrawCharacter(Unit u, float y, GameSession s, FrameLayout lay)
        {
            var r = new Rect(X, y, W, H);
            var combat = Hud.Combat;
            var battle = combat != null ? combat.Battle : null;
            bool active = battle != null && !battle.IsOver && HudPresented.ActiveUnit(battle) == u;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == u;
            var pick = Hud.FieldPick;
            bool pickable = pick != null && Hud.IsValidPickTarget(u);
            var classCol = Hud.UnitColor(u);
            bool dead = HudPresented.Dead(u), downed = !dead && HudPresented.Downed(u);

            // backdrop & highlights
            if (active)
            {
                float p = HudDraw.Pulse(4f, 0.35f, 0.7f);
                HudDraw.Glow(new Rect(r.x - 26f, r.y - 22f, r.width + 52f, r.height + 44f), new Color(1f, 0.82f, 0.4f, 0.32f * p));
            }
            HudDraw.Frame(r, 0.8f, active ? new Color(1f, 0.85f, 0.45f, 0.95f) : (Color?)null);
            if (active) HudDraw.Ring(r, new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)), 8, true);
            else if (selected) HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.6f), 8);
            if (pick != null)
            {
                if (pickable) HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.45f, 1f)), 8, true);
                else HudDraw.Fill(r, new Color(0f, 0f, 0f, 0.35f), 8);
            }
            DrawTargetState(r, Hud.TargetStateOf(u));

            // portrait + overlays
            var pr = new Rect(r.x + 9f, r.y + 10f, Portrait, Portrait);
            HudDraw.Portrait(pr, u, dead || downed ? FrameDead : classCol, dead || downed);
            if (dead)
            {
                HudDraw.Glyph(new Rect(pr.x + 17f, pr.y + 8f, 36f, 36f), "glyph_skull", new Color(1f, 1f, 1f, 0.85f));
                HudDraw.Text(new Rect(pr.x, pr.yMax - 24f, pr.width, 20f), "Dead", HudStyles.TinyCenter, Ui.Bad);
            }
            else if (downed)
            {
                HudDraw.Text(new Rect(pr.x, pr.yMax - 26f, pr.width, 22f), "Downed", HudStyles.TinyCenter, Ui.Bad);
            }
            // leader crown
            if (s.Leader == u && s.Party.Count > 1)
            {
                var cr = new Rect(pr.x - 8f, pr.y - 12f, 26f, 26f);
                HudDraw.Glyph(cr, "glyph_crown", Ui.Gold);
                if (HudDraw.Hover(cr)) Ui.TooltipFor(cr, "Party leader — the camera follows them and they lead the walk.");
            }
            // level badge
            var labels = HudText.For(u);
            var lb = new Rect(pr.xMax - 22f, pr.yMax - 20f, 26f, 24f);
            HudDraw.Fill(lb, new Color(0.08f, 0.06f, 0.13f, 0.95f), 8);
            HudDraw.Ring(lb, new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.85f), 8);
            HudDraw.Text(lb, labels.Level.Get(u.Level), HudStyles.TinyCenter, Ui.TextLight, false);

            float cx = pr.xMax + 12f, cw = r.xMax - cx - 10f;
            // name
            bool companion = u.Companion != null || (u.Kind == UnitKind.Companion);
            float nameW = cw - (companion ? 60f : 0f);
            HudDraw.Text(new Rect(cx, r.y + 6f, nameW, 22f), Hud.NameOf(u), HudStyles.Name, dead ? Hud.Muted : classCol);

            // auto-play toggle (companions)
            bool overToggle = false;
            if (companion)
            {
                var ar = new Rect(r.xMax - 64f, r.y + 7f, 54f, 20f);
                bool on = u.AutoPlay;
                overToggle = DrawAutoToggle(ar, u, on, true,
                    on ? "<b>Auto-play on</b>\nThe companion's AI plays its turns in combat (and its pet's). Click to take control."
                       : "<b>Auto-play off</b>\nYou control this companion in combat. Click to let its AI play.");
            }

            // health (+ absorb)
            var hr = new Rect(cx, r.y + 29f, cw, 19f);
            DrawHealth(hr, u, labels.Health, HudStyles.TinyCenter);

            // resource
            var res = u.PowerType;
            float maxRes = res != ResourceType.None ? u.MaxResource(res) : 0f;
            var rr = new Rect(cx, r.y + 51f, cw, 16f);
            if (maxRes > 0f)
            {
                float cur = u.GetResource(res);
                HudDraw.Bar(rr, cur / maxRes, Ui.ResourceColor(res));
                string txt = res == ResourceType.Mana ? labels.Resource.Get(Mathf.FloorToInt(cur), Mathf.RoundToInt(maxRes)) : labels.Rage.Get(Mathf.FloorToInt(cur));
                HudDraw.Text(new Rect(rr.x, rr.y - 1f, rr.width, rr.height + 2f), txt, HudStyles.TinyCenter, Ui.TextLight);
                if (HudDraw.Hover(rr)) Ui.TooltipFor(rr, HudText.ResourceName(res));
            }

            // third row: pending cast bar, else combo pips (rogues) / Soul Shards (warlocks)
            var row3 = new Rect(cx, r.y + 70f, cw, 12f);
            if (u.Pending != null && u.Pending.Ability != null) DrawCastBar(row3, u.Pending, true);
            else if (u.ClassId == ClassId.Rogue || u.ComboPoints > 0) DrawCombo(row3, u);
            else if (u.ClassId == ClassId.Warlock) DrawShards(row3);

            // XP (main character, outside combat)
            if (u.IsMainCharacter && battle == null && Hud.Db != null)
            {
                int need = Progression.XpToNextLevel(Hud.Db, u.Level);
                if (need > 0 && need < int.MaxValue)
                {
                    var xr = new Rect(cx, r.yMax - 6f, cw, 4f);
                    HudDraw.Solid(xr, new Color(0.04f, 0.03f, 0.08f, 0.8f));
                    HudDraw.Solid(new Rect(xr.x, xr.y, xr.width * Mathf.Clamp01((float)u.Xp / need), xr.height), Hud.Xp);
                    var hov = new Rect(xr.x, xr.y - 3f, xr.width, xr.height + 6f);
                    if (HudDraw.Hover(hov)) Ui.TooltipFor(hov, "Experience: " + HudText.Int(u.Xp) + " / " + HudText.Int(need));
                }
            }

            // aggro: enemies currently attacking this member (WoW-style threat warning)
            if (battle != null && !battle.IsOver && !dead)
            {
                int attackers = AttackersOf(u, battle);
                if (attackers > 0)
                {
                    HudDraw.Ring(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(1f, 0.3f, 0.25f, HudDraw.Pulse(3f, 0.35f, 0.75f)), 8);
                    var ab = new Rect(pr.xMax - 32f, pr.y + 2f, 30f, 18f);
                    HudDraw.Fill(ab, new Color(0.55f, 0.08f, 0.08f, 0.92f), 5);
                    HudDraw.Glyph(new Rect(ab.x + 2f, ab.y + 2f, 14f, 14f), "glyph_swords", Color.white);
                    HudDraw.Text(new Rect(ab.x + 15f, ab.y - 1f, 14f, 20f), HudText.Int(attackers), HudStyles.TinyCenter, Color.white, false);
                    if (HudDraw.Hover(ab)) Ui.TooltipFor(ab, attackers == 1 ? "An enemy is attacking " + Hud.NameOf(u) + "." : attackers + " enemies are attacking " + Hud.NameOf(u) + ".");
                }
            }

            // click → select / pick; tooltip on the portrait
            if (HudDraw.Hover(pr)) Ui.TooltipFor(pr, TipFor(u));
            Ui.Block(r);
            if (!overToggle && HudDraw.Click(r))
            {
                var unit = u;
                if (pick != null) Hud.ResolveFieldPick(unit);
                else Hud.ClickUnit(unit);
                Ui.Sfx?.Invoke("ui_click");
            }

            float y2 = r.yMax + 3f;
            // auras
            y2 = DrawAuras(u, X + 2f, y2, W - 4f, lay.AuraRows, lay.Buffs);
            if (!lay.Subs) return y2;   // compact column (see Layouts): no pet / totem rows
            // pets / controlled summons
            CollectSubs(u, battle);
            for (int i = 0; i < subs.Count; i++) y2 = DrawPet(subs[i], y2 + 2f, battle, lay.PetAuraRows, lay.Buffs) + 1f;
            // dead hunter pet hint
            if (ShowDeadPetHint(u))
            {
                var dr = new Rect(X + PetIndent, y2 + 2f, PetW, DeadPetHintH);
                string pn = string.IsNullOrEmpty(u.HunterPet.Name) ? "Your pet" : u.HunterPet.Name;
                if (!deadPetText.TryGetValue(pn, out var line)) deadPetText[pn] = line = pn + " is dead — Revive Pet";
                HudDraw.Text(dr, line, HudStyles.Small, Hud.Muted);
                y2 = dr.yMax;
            }
            // totems
            if (u.Totems.Count > 0) y2 = DrawTotems(u, X + PetIndent, y2 + 3f);
            return y2;
        }

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

        /// <summary>The AUTO pill (companions, pets). Returns true while the mouse is over it (the frame click is skipped).</summary>
        static bool DrawAutoToggle(Rect ar, Unit u, bool on, bool enabled, string tip)
        {
            HudDraw.Fill(ar, on ? new Color(0.2f, 0.45f, 0.22f, enabled ? 0.95f : 0.6f) : new Color(0.16f, 0.13f, 0.24f, 0.9f), 5);
            HudDraw.Ring(ar, on ? new Color(AutoOn.r, AutoOn.g, AutoOn.b, enabled ? 1f : 0.45f) : new Color(1f, 1f, 1f, 0.3f), 5);
            HudDraw.Text(ar, "AUTO", HudStyles.TinyCenter, on ? (enabled ? Color.white : new Color(1f, 1f, 1f, 0.6f)) : Hud.Muted, false);
            if (HudDraw.Hover(ar)) Ui.TooltipFor(ar, tip);
            Ui.Block(ar);
            bool over = HudDraw.Hover(ar);
            if (enabled && HudDraw.Click(ar))
            {
                var unit = u;
                bool want = !on;
                Hud.Post(() => SetAutoPlay(unit, want));
                Ui.Sfx?.Invoke("ui_click");
            }
            return over;
        }

        static void SetAutoPlay(Unit u, bool on)
        {
            var c = Hud.Combat;
            if (c != null) c.SetAutoPlay(u, on);
            else Hud.Session?.SetAutoPlay(u, on);
        }

        /// <summary>Combat targeting from the frames: valid targets pulse green, reachable ones (a click walks into range
        /// first) pulse softer, others are dimmed.</summary>
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

        string TipFor(Unit u)
        {
            var tst = Hud.TargetStateOf(u);
            if (tst != Hud.TargetState.None)
            {
                // targeting in combat: the tooltip says what the click does (no cache: the state changes as you move)
                var cls0 = u.Class != null ? u.Class.name : (u.Creature != null ? u.Creature.name : "");
                return "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + " " + cls0 + "\n" + TargetHint(u, tst);
            }
            if (tipUnit == u && tipFrame > Time.frameCount - 15) return tipText;
            tipUnit = u;
            tipFrame = Time.frameCount;
            var cls = u.Class != null ? u.Class.name : (u.Creature != null ? u.Creature.name : "");
            string role = "";
            try { role = u.IsCharacter ? UiText.Spaced(u.Role.ToString()) : ""; } catch (Exception) { }
            // the selected companion: clicking them in the world again talks to them (GameFlow.ClickView → TalkToCompanion)
            var flow = Hud.Flow;
            string click = Hud.FieldPick != null ? "Click to choose this party member."
                         : flow != null && flow.Selected == u && flow.CanTalkTo(u) ? "Selected. Click " + Hud.NameOf(u) + " in the world to talk."
                         : "Click to select.";
            tipText = "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + " " + cls + (role.Length > 0 ? "  ·  " + role : "") +
                      "\n" + Ui.Rich(click, Hud.Muted);
            return tipText;
        }

        // ================================================================ bars

        public static void DrawHealth(Rect hr, Unit u, HudText.Pair label, GUIStyle textStyle)
        {
            float max = Mathf.Max(1f, u.MaxHealth);
            float hp = HudPresented.Health(u);   // as presented: drops when the blow lands on screen
            float pct = hp / max;
            HudDraw.Bar(hr, pct, HudPresented.Dead(u) || HudPresented.Downed(u) ? FrameDead : Hud.HealthColor(pct));
            float absorb = Hud.AbsorbOf(u);
            if (absorb > 0f)
            {
                float a0 = pct, a1 = Mathf.Min(1f, (hp + absorb) / max);
                if (a1 - a0 < 0.02f) { a0 = Mathf.Max(0f, a1 - 0.04f); }
                HudDraw.BarSegment(hr, a0, a1, Hud.Absorb);
            }
            if (hr.height >= 12f && label != null)
                HudDraw.Text(new Rect(hr.x, hr.y - 1f, hr.width, hr.height + 2f), label.Get(Mathf.CeilToInt(hp), Mathf.RoundToInt(max)), textStyle, Ui.TextLight);
            if (HudDraw.Hover(hr) && absorb > 0f) Ui.TooltipFor(hr, "Health " + Mathf.CeilToInt(hp) + " / " + Mathf.RoundToInt(max) + "\nAbsorbs " + Mathf.RoundToInt(absorb) + " damage");
        }

        public static void DrawCastBar(Rect r, PendingCast p, bool compact)
        {
            var a = p.Ability;
            var col = Hud.SchoolCol(a.school);
            float progress = Hud.PendingProgress(p);
            HudDraw.Bar(r, Mathf.Max(0.04f, progress), new Color(col.r, col.g, col.b, 0.92f));
            HudDraw.Ring(r, new Color(col.r, col.g, col.b, HudDraw.Pulse(5f, 0.4f, 0.9f)), 5);
            if (r.height >= 11f)
                HudDraw.Text(new Rect(r.x + 4f, r.y - 2f, r.width - 8f, r.height + 4f), a.name, compact ? HudStyles.Tiny : HudStyles.NameSmall, Ui.TextLight);
            if (HudDraw.Hover(r))
                Ui.TooltipFor(r, "<b>Casting " + a.name + "</b>\n" + HudText.Secs(p.RemainingTime) + " s left — resolves at the start of its next turn." +
                                 (p.Channel ? "\nChannelled: " + p.TicksLeft + " tick(s) remaining." : "") +
                                 "\n" + Ui.Rich("Damage pushes it back; stuns, silences and interrupts cancel it.", Hud.Muted));
        }

        static void DrawCombo(Rect row, Unit u)
        {
            // the count the rules use (Battle.ComboPointsOn: points stay with the rogue across targets until spent)
            var b = Hud.Combat != null ? Hud.Combat.Battle : null;
            int cp = u.ComboPoints;
            try { if (b != null) cp = b.ComboPointsOn(u, null); } catch (Exception) { }
            cp = Mathf.Clamp(cp, 0, 5);
            const float d = 12f, gap = 4f;
            for (int i = 0; i < 5; i++)
                HudDraw.Pip(new Rect(row.x + i * (d + gap), row.y, d, d), Hud.C("#ffcf4d"), i < cp);
            var hr = new Rect(row.x, row.y, 5 * (d + gap), d);
            if (HudDraw.Hover(hr))
                Ui.TooltipFor(hr, "Combo points: " + cp + (u.ComboTarget != null && cp > 0 ? " (last built on " + Hud.NameOf(u.ComboTarget) + ")" : "") +
                                  "\n" + Ui.Rich("Builders add a point; finishers spend them all, hitting much harder at 3–5. Points stay with you for the whole fight, even when the target falls.", Hud.Muted));
        }

        void DrawShards(Rect row)
        {
            int n = Hud.SoulShards;
            var col = n > 0 ? ShardCol : Ui.Bad;
            HudDraw.Glyph(new Rect(row.x - 1f, row.y - 2f, 16f, 16f), "glyph_soul_shard", col);
            string txt = n == 1 ? "1 Soul Shard" : shardsText.Get(n);
            HudDraw.Text(new Rect(row.x + 18f, row.y - 3f, row.width - 18f, 18f), txt, HudStyles.Tiny, col);
            var hr = new Rect(row.x - 1f, row.y - 3f, Mathf.Min(row.width, 150f), 18f);
            if (HudDraw.Hover(hr))
                Ui.TooltipFor(hr, "<b>Soul Shards: " + n + "</b> (in the party's bags)\n" +
                                  Ui.Rich("Soul Fire, Shadowburn, demon summons, Soulstones and Healthstones consume one. " +
                                          "Drain Soul on a dying enemy (or Shadowburn's kill) creates one.", Hud.Muted));
        }

        // ================================================================ auras

        /// <summary>Draws the unit's visible debuffs and (when buffs is set) buffs in up to maxRows rows, debuffs first.
        /// Returns the bottom y (AuraBlockHeight below y).</summary>
        float DrawAuras(Unit u, float x, float y, float width, int maxRows, bool buffs)
        {
            if (maxRows <= 0) return y;
            auras.Clear();
            var list = u.Auras;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || a.Def == null || a.Def.hidden || a.IsPassive) continue;
                if (a.IsDebuff) auras.Add(a);
            }
            int debuffs = auras.Count;
            for (int i = 0; buffs && i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || a.Def == null || a.Def.hidden || a.IsPassive) continue;
                if (!a.IsDebuff) auras.Add(a);
            }
            if (auras.Count == 0) return y;
            int perRow = AurasPerRow(width);
            int n = Mathf.Min(auras.Count, perRow * maxRows);
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                var ar = new Rect(x + col * (AuraSize + AuraGap), y + row * (AuraSize + AuraGap + 1f), AuraSize, AuraSize);
                DrawAuraIcon(ar, auras[i], i < debuffs, null, true);
            }
            return y + AuraBlockHeight(auras.Count, width, maxRows);
        }

        /// <summary>
        /// Aura icon with remaining time, stacks and tooltip. mine = highlight auras from this caster. cancellable = the
        /// party's own frames: the icon blocks world clicks and a right-click cancels a removable own buff (WoW).
        /// </summary>
        public static void DrawAuraIcon(Rect ar, AuraInstance a, bool debuff, Unit mine, bool cancellable = false)
        {
            var def = a.Def;
            var school = Hud.SchoolCol(def.school);
            Color frame = debuff ? DebuffColor(def.dispel) : Color.Lerp(school, Hud.BuffFrame, 0.35f);
            bool faded = mine != null && a.Caster != mine;
            HudDraw.Icon(ar, string.IsNullOrEmpty(def.icon) ? "glyph_aura" : def.icon, frame, false, faded ? 0.7f : 1f);
            if (a.Duration > 0f && a.Remaining > 0f && !a.IsPermanent)
            {
                var tr = new Rect(ar.x - 4f, ar.yMax - 13f, ar.width + 8f, 16f);
                HudDraw.Text(tr, HudText.Duration(a.Remaining), HudStyles.TinyCenter, a.Remaining <= 6f ? Hud.C("#ffd27a") : Ui.TextLight);
            }
            if (a.Stacks > 1)
                HudDraw.Text(new Rect(ar.x, ar.y - 4f, ar.width - 1f, 16f), HudText.Int(a.Stacks), HudStyles.TinyRight, Ui.TextLight);
            else if (a.Charges > 0)
                HudDraw.Text(new Rect(ar.x, ar.y - 4f, ar.width - 1f, 16f), HudText.Int(a.Charges), HudStyles.TinyRight, Hud.C("#9fe3e0"));
            bool canCancel = cancellable && !debuff && Hud.CanCancel(a);
            if (cancellable) Ui.Block(ar);
            if (HudDraw.Hover(ar))
            {
                string tip = UiText.Aura(a);
                if (a.Caster != null && a.Caster != a.Bearer) tip += "\n" + Ui.Rich("From " + Hud.NameOf(a.Caster), Hud.Muted);
                if (canCancel) tip += "\n" + Ui.Rich("Right-click to cancel.", Hud.Muted);
                Ui.TooltipFor(ar, tip);
            }
            if (canCancel && HudDraw.Click(ar, 1))
            {
                var unit = a.Bearer;
                var aura = a;
                Hud.Post(() => Hud.CancelAura(unit, aura));
            }
        }

        public static Color DebuffColor(DispelType d)
        {
            switch (d)
            {
                case DispelType.Magic: return Hud.C("#3d8ff0");
                case DispelType.Curse: return Hud.C("#a64dff");
                case DispelType.Poison: return Hud.C("#2ea84f");
                case DispelType.Disease: return Hud.C("#a67c35");
                default: return Hud.DebuffRed;
            }
        }

        // ================================================================ pets & totems

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
                    if (subs.Count < 3) subs.Add(p);
                }
                return;
            }
            if (owner.Pet != null && !owner.Pet.Dead) subs.Add(owner.Pet);
            for (int i = 0; i < owner.Summons.Count && subs.Count < 3; i++)
            {
                var p = owner.Summons[i];
                if (p != null && p != owner.Pet && !p.Dead && !p.IsTotem) subs.Add(p);
            }
        }

        float DrawPet(Unit p, float y, Battle battle, int auraRows, bool buffs)
        {
            var r = new Rect(X + PetIndent, y, PetW, PetH);
            bool active = battle != null && !battle.IsOver && HudPresented.ActiveUnit(battle) == p;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == p;
            var pick = Hud.FieldPick;
            HudDraw.Frame(r, 0.72f, active ? new Color(1f, 0.85f, 0.45f, 0.95f) : (Color?)null);
            if (active) HudDraw.Ring(r, new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)), 8, true);
            else if (selected) HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.55f), 8);
            if (pick != null && Hud.IsValidPickTarget(p)) HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.45f, 1f)), 8, true);
            var tstate = Hud.TargetStateOf(p);
            DrawTargetState(r, tstate);

            var pr = new Rect(r.x + 4f, r.y + 4f, PetH - 8f, PetH - 8f);
            bool down = HudPresented.Downed(p) || HudPresented.Dead(p);
            HudDraw.Portrait(pr, p, down ? FrameDead : Hud.UnitColor(p), down);
            float cx = pr.xMax + 7f, cw = r.xMax - cx - 7f;
            string name = Hud.NameOf(p);
            if (p.OriginalTeam.HasValue) name += " (controlled)";
            // AUTO toggle for the party's pets (hand the pet to the AI while you play its owner; the owner's AUTO overrides)
            bool petToggle = p.Kind == UnitKind.Pet && p.Owner != null && p.Team == Team.Player && !p.OriginalTeam.HasValue;
            bool overToggle = false;
            if (petToggle)
            {
                var tr = new Rect(r.xMax - 56f, r.y + 3f, 50f, 17f);
                bool ownerAuto = p.Owner.AutoPlay;
                bool on = p.AutoPlay || ownerAuto;
                string tip = !HudDraw.Hover(tr) ? null   // built only while hovered (no per-frame string)
                           : ownerAuto ? "<b>Auto-play on</b>\nFollows " + Hud.NameOf(p.Owner) + "'s auto-play. Turn that off to choose for the pet."
                           : on ? "<b>Auto-play on</b>\nThe pet's AI plays its turns in combat. Click to take control."
                                : "<b>Auto-play off</b>\nYou control this pet in combat. Click to let its AI play its turns.";
                overToggle = DrawAutoToggle(tr, p, on, !ownerAuto, tip);
            }
            HudDraw.Text(new Rect(cx, r.y + 1f, cw - (petToggle ? 56f : 0f), 18f), name, HudStyles.NameSmall, Hud.UnitColor(p));
            var labels = HudText.For(p);
            DrawHealth(new Rect(cx, r.y + 19f, cw, 9f), p, null, null);
            var res = p.PowerType;
            float maxRes = res != ResourceType.None ? p.MaxResource(res) : 0f;
            if (maxRes > 0f) HudDraw.Bar(new Rect(cx, r.y + 30f, cw, 5f), p.GetResource(res) / maxRes, Ui.ResourceColor(res));
            if (p.Pending != null && p.Pending.Ability != null) DrawCastBar(new Rect(cx, r.y + 29f, cw, 7f), p.Pending, true);

            if (HudDraw.Hover(r) && !overToggle)
                Ui.TooltipFor(r, "<b>" + name + "</b>\nHealth " + labels.PetHealth.Get(Mathf.CeilToInt(HudPresented.Health(p)), Mathf.RoundToInt(p.MaxHealth)) +
                                 (maxRes > 0f ? "\n" + HudText.ResourceName(res) + " " + Mathf.FloorToInt(p.GetResource(res)) + " / " + Mathf.RoundToInt(maxRes) : "") +
                                 (p.Lifetime > 0f ? "\n" + Ui.Rich(UiText.Duration(p.Lifetime) + " remaining", Hud.Muted) : "") +
                                 "\n" + (tstate != Hud.TargetState.None ? TargetHint(p, tstate) : Ui.Rich("Click to select (its abilities appear on the action bar).", Hud.Muted)));
            Ui.Block(r);
            if (!overToggle && HudDraw.Click(r))
            {
                var unit = p;
                if (pick != null) Hud.ResolveFieldPick(unit);
                else Hud.ClickUnit(unit);
                Ui.Sfx?.Invoke("ui_click");
            }
            float y2 = DrawAuras(p, r.x + 2f, r.yMax + 2f, r.width - 4f, auraRows, buffs);
            return Mathf.Max(r.yMax, y2);
        }

        static float DrawTotems(Unit u, float x, float y)
        {
            const float s = TotemSize;
            int i = 0;
            foreach (var kv in u.Totems)
            {
                var t = kv.Value;
                if (t == null || t.Dead) continue;
                var r = new Rect(x + i * (s + 4f), y, s, s);
                string element = kv.Key ?? "";
                string glyph = TotemGlyph(element);
                HudDraw.Icon(r, glyph, TotemColor(element));
                if (t.Lifetime > 0f) HudDraw.Text(new Rect(r.x - 4f, r.yMax - 13f, r.width + 8f, 16f), HudText.Duration(t.Lifetime), HudStyles.TinyCenter, Ui.TextLight);
                if (HudDraw.Hover(r))
                    Ui.TooltipFor(r, "<b>" + Hud.NameOf(t) + "</b>\n" + element + " totem · Health " + Mathf.CeilToInt(t.Health) + " / " + Mathf.RoundToInt(t.MaxHealth) +
                                     (t.Lifetime > 0f ? "\n" + Ui.Rich(UiText.Duration(t.Lifetime) + " remaining", Hud.Muted) : ""));
                i++;
            }
            return i > 0 ? y + s + 2f : y;
        }

        // element → (glyph, colour) without lower-casing a string per totem per event
        static int TotemElement(string element)
        {
            if (string.IsNullOrEmpty(element)) return -1;
            if (string.Equals(element, "earth", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(element, "fire", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(element, "water", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(element, "air", StringComparison.OrdinalIgnoreCase)) return 3;
            return -1;
        }

        static string TotemGlyph(string element)
        {
            switch (TotemElement(element))
            {
                case 0: return "glyph_totem_earth";
                case 1: return "glyph_totem_fire";
                case 2: return "glyph_totem_water";
                case 3: return "glyph_totem_air";
                default: return "glyph_totem";
            }
        }

        static Color TotemColor(string element)
        {
            switch (TotemElement(element))
            {
                case 0: return Hud.C("#b08a52");
                case 1: return Hud.SchoolCol(School.Fire);
                case 2: return Hud.SchoolCol(School.Frost);
                case 3: return Hud.C("#d6e8f0");
                default: return Hud.SchoolCol(School.Nature);
            }
        }
    }
}
