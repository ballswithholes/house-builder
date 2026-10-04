// Party frames (left column): portrait with class-colour frame, name, level badge, health (+absorb) and
// resource bars, combo-point pips, pending-cast bar, XP (main character), buffs/debuffs with time & stacks,
// pet sub-frames, totems, downed/dead states, auto-play toggle (companions) and the leader's crown.
// Click selects (GameFlow.Select); in the out-of-combat "choose a party member" mode the click casts instead.
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
        public bool Visible => Hud.WorldHud;
        public bool Modal => false;

        const float X = 14f, Y = 14f, W = 300f, H = 90f, Portrait = 70f;
        const float PetW = 236f, PetH = 38f, PetIndent = 44f;
        const float AuraSize = 24f, AuraGap = 3f;

        static readonly Color FrameDead = new Color(0.55f, 0.52f, 0.6f, 1f);
        static readonly Color AutoOn = Ui.Hex("#7fd47a");

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

                float y = Y;
                for (int i = 0; i < chars.Count; i++)
                {
                    y = DrawCharacter(chars[i], y, s);
                    y += 10f;
                    if (y > Ui.Height - 260f) break;   // never run into the bottom HUD block
                }
            }
            catch (Exception e) { Hud.LogOnce("party:" + e.GetType().Name, "Party frames: " + e); }
            finally { HudDraw.EndLayer(layer); }
        }

        // ================================================================ character frame

        float DrawCharacter(Unit u, float y, GameSession s)
        {
            var r = new Rect(X, y, W, H);
            var combat = Hud.Combat;
            var battle = combat != null ? combat.Battle : null;
            bool active = battle != null && battle.ActiveUnit == u && !battle.IsOver;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == u;
            var pick = Hud.FieldPick;
            bool pickable = pick != null && Hud.IsValidPickTarget(u);
            var classCol = Hud.UnitColor(u);
            bool dead = u.Dead, downed = u.Downed && !u.Dead;

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
            float nameW = cw - (companion ? 54f : 0f);
            HudDraw.Text(new Rect(cx, r.y + 6f, nameW, 22f), Hud.NameOf(u), HudStyles.Name, dead ? Hud.Muted : classCol);

            // auto-play toggle (companions)
            bool overToggle = false;
            if (companion)
            {
                var ar = new Rect(r.xMax - 58f, r.y + 8f, 48f, 18f);
                bool on = u.AutoPlay;
                HudDraw.Fill(ar, on ? new Color(0.2f, 0.45f, 0.22f, 0.95f) : new Color(0.16f, 0.13f, 0.24f, 0.9f), 5);
                HudDraw.Ring(ar, on ? AutoOn : new Color(1f, 1f, 1f, 0.3f), 5);
                HudDraw.Text(ar, "AUTO", HudStyles.TinyCenter, on ? Color.white : Hud.Muted, false);
                if (HudDraw.Hover(ar))
                    Ui.TooltipFor(ar, on ? "<b>Auto-play on</b>\nThe companion's AI plays its turns in combat (and its pet's). Click to take control."
                                         : "<b>Auto-play off</b>\nYou control this companion in combat. Click to let its AI play.");
                Ui.Block(ar);
                overToggle = HudDraw.Hover(ar);
                if (HudDraw.Click(ar))
                {
                    var unit = u;
                    bool want = !on;
                    Hud.Post(() => SetAutoPlay(unit, want));
                    Ui.Sfx?.Invoke("ui_click");
                }
            }

            // health (+ absorb)
            var hr = new Rect(cx, r.y + 30f, cw, 18f);
            DrawHealth(hr, u, labels.Health, HudStyles.TinyCenter);

            // resource
            var res = u.PowerType;
            float maxRes = res != ResourceType.None ? u.MaxResource(res) : 0f;
            var rr = new Rect(cx, r.y + 51f, cw, 13f);
            if (maxRes > 0f)
            {
                float cur = u.GetResource(res);
                HudDraw.Bar(rr, cur / maxRes, Ui.ResourceColor(res));
                string txt = res == ResourceType.Mana ? labels.Resource.Get(Mathf.FloorToInt(cur), Mathf.RoundToInt(maxRes)) : labels.Rage.Get(Mathf.FloorToInt(cur));
                HudDraw.Text(new Rect(rr.x, rr.y - 1f, rr.width, rr.height + 2f), txt, HudStyles.TinyCenter, Ui.TextLight);
                if (HudDraw.Hover(rr)) Ui.TooltipFor(rr, HudText.ResourceName(res));
            }

            // third row: pending cast bar, else combo pips
            var row3 = new Rect(cx, r.y + 68f, cw, 12f);
            if (u.Pending != null && u.Pending.Ability != null) DrawCastBar(row3, u.Pending, true);
            else if (u.ClassId == ClassId.Rogue || u.ComboPoints > 0) DrawCombo(row3, u);

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
                    HudDraw.Text(new Rect(ab.x + 15f, ab.y, 14f, 18f), HudText.Int(attackers), HudStyles.TinyCenter, Color.white, false);
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
            y2 = DrawAuras(u, X + 2f, y2, W - 4f);
            // pets / controlled summons
            CollectSubs(u, battle);
            for (int i = 0; i < subs.Count; i++) y2 = DrawPet(subs[i], y2 + 2f, battle) + 1f;
            // dead hunter pet hint
            if (subs.Count == 0 && u.HunterPet != null && u.HunterPet.Dead && u.Pet == null)
            {
                var dr = new Rect(X + PetIndent, y2 + 2f, PetW, 18f);
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

        static void SetAutoPlay(Unit u, bool on)
        {
            var c = Hud.Combat;
            if (c != null) c.SetAutoPlay(u, on);
            else Hud.Session?.SetAutoPlay(u, on);
        }

        string TipFor(Unit u)
        {
            if (tipUnit == u && tipFrame > Time.frameCount - 15) return tipText;
            tipUnit = u;
            tipFrame = Time.frameCount;
            var cls = u.Class != null ? u.Class.name : (u.Creature != null ? u.Creature.name : "");
            string role = "";
            try { role = u.IsCharacter ? UiText.Spaced(u.Role.ToString()) : ""; } catch (Exception) { }
            tipText = "<b>" + Hud.NameOf(u) + "</b>\nLevel " + u.Level + " " + cls + (role.Length > 0 ? "  ·  " + role : "") +
                      "\n" + Ui.Rich(Hud.FieldPick != null ? "Click to choose this party member." : "Click to select.", Hud.Muted);
            return tipText;
        }

        // ================================================================ bars

        public static void DrawHealth(Rect hr, Unit u, HudText.Pair label, GUIStyle textStyle)
        {
            float max = Mathf.Max(1f, u.MaxHealth);
            float hp = Mathf.Max(0f, u.Health);
            float pct = hp / max;
            HudDraw.Bar(hr, pct, u.Dead || u.Downed ? FrameDead : Hud.HealthColor(pct));
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
            float total = Hud.PendingTotal(p);
            float progress = 1f - Mathf.Clamp01(p.RemainingTime / total);
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
            int cp = Mathf.Clamp(u.ComboPoints, 0, 5);
            const float d = 12f, gap = 4f;
            for (int i = 0; i < 5; i++)
                HudDraw.Pip(new Rect(row.x + i * (d + gap), row.y, d, d), Hud.C("#ffcf4d"), i < cp);
            var hr = new Rect(row.x, row.y, 5 * (d + gap), d);
            if (HudDraw.Hover(hr))
                Ui.TooltipFor(hr, "Combo points: " + cp + (u.ComboTarget != null && cp > 0 ? " on " + Hud.NameOf(u.ComboTarget) : "") +
                                  "\n" + Ui.Rich("Builders add points to the target; finishers spend them all.", Hud.Muted));
        }

        // ================================================================ auras

        /// <summary>Draws the unit's visible buffs/debuffs in rows (debuffs first). Returns the bottom y.</summary>
        float DrawAuras(Unit u, float x, float y, float width)
        {
            auras.Clear();
            var list = u.Auras;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || a.Def == null || a.Def.hidden || a.IsPassive) continue;
                if (a.IsDebuff) auras.Add(a);
            }
            int debuffs = auras.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || a.Def == null || a.Def.hidden || a.IsPassive) continue;
                if (!a.IsDebuff) auras.Add(a);
            }
            if (auras.Count == 0) return y;
            int perRow = Mathf.Max(1, (int)((width + AuraGap) / (AuraSize + AuraGap)));
            int max = perRow * 2;
            int n = Mathf.Min(auras.Count, max);
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                var ar = new Rect(x + col * (AuraSize + AuraGap), y + row * (AuraSize + AuraGap + 1f), AuraSize, AuraSize);
                DrawAuraIcon(ar, auras[i], i < debuffs, null);
            }
            int rows = (n + perRow - 1) / perRow;
            return y + rows * (AuraSize + AuraGap + 1f);
        }

        /// <summary>Aura icon with remaining time, stacks and tooltip. mine = highlight auras from this caster.</summary>
        public static void DrawAuraIcon(Rect ar, AuraInstance a, bool debuff, Unit mine)
        {
            var def = a.Def;
            var school = Hud.SchoolCol(def.school);
            Color frame = debuff ? DebuffColor(def.dispel) : Color.Lerp(school, Hud.BuffFrame, 0.35f);
            bool faded = mine != null && a.Caster != mine;
            HudDraw.Icon(ar, string.IsNullOrEmpty(def.icon) ? "glyph_aura" : def.icon, frame, false, faded ? 0.7f : 1f);
            if (a.Duration > 0f && a.Remaining > 0f && !a.IsPermanent)
            {
                var tr = new Rect(ar.x - 4f, ar.yMax - 11f, ar.width + 8f, 12f);
                HudDraw.Text(tr, HudText.Duration(a.Remaining), HudStyles.TinyCenter, a.Remaining <= 6f ? Hud.C("#ffd27a") : Ui.TextLight);
            }
            if (a.Stacks > 1)
                HudDraw.Text(new Rect(ar.x, ar.y - 2f, ar.width - 1f, 12f), HudText.Int(a.Stacks), HudStyles.TinyRight, Ui.TextLight);
            else if (a.Charges > 0)
                HudDraw.Text(new Rect(ar.x, ar.y - 2f, ar.width - 1f, 12f), HudText.Int(a.Charges), HudStyles.TinyRight, Hud.C("#9fe3e0"));
            if (HudDraw.Hover(ar))
            {
                string tip = UiText.Aura(a);
                if (a.Caster != null && a.Caster != a.Bearer) tip += "\n" + Ui.Rich("From " + Hud.NameOf(a.Caster), Hud.Muted);
                Ui.TooltipFor(ar, tip);
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
                    if (p == null || p.Owner != owner || p.IsTotem || p.Dead) continue;
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

        float DrawPet(Unit p, float y, Battle battle)
        {
            var r = new Rect(X + PetIndent, y, PetW, PetH);
            bool active = battle != null && battle.ActiveUnit == p && !battle.IsOver;
            bool selected = Hud.Flow != null && Hud.Flow.Selected == p;
            var pick = Hud.FieldPick;
            HudDraw.Frame(r, 0.72f, active ? new Color(1f, 0.85f, 0.45f, 0.95f) : (Color?)null);
            if (active) HudDraw.Ring(r, new Color(1f, 0.85f, 0.45f, HudDraw.Pulse(4f, 0.5f, 1f)), 8, true);
            else if (selected) HudDraw.Ring(r, new Color(1f, 1f, 1f, 0.55f), 8);
            if (pick != null && Hud.IsValidPickTarget(p)) HudDraw.Ring(r, new Color(0.5f, 1f, 0.55f, HudDraw.Pulse(6f, 0.45f, 1f)), 8, true);

            var pr = new Rect(r.x + 4f, r.y + 4f, PetH - 8f, PetH - 8f);
            bool down = p.Downed || p.Dead;
            HudDraw.Portrait(pr, p, down ? FrameDead : Hud.UnitColor(p), down);
            float cx = pr.xMax + 7f, cw = r.xMax - cx - 7f;
            string name = Hud.NameOf(p);
            if (p.OriginalTeam.HasValue) name += " (controlled)";
            HudDraw.Text(new Rect(cx, r.y + 2f, cw, 16f), name, HudStyles.NameSmall, Hud.UnitColor(p));
            var labels = HudText.For(p);
            DrawHealth(new Rect(cx, r.y + 19f, cw, 9f), p, null, null);
            var res = p.PowerType;
            float maxRes = res != ResourceType.None ? p.MaxResource(res) : 0f;
            if (maxRes > 0f) HudDraw.Bar(new Rect(cx, r.y + 30f, cw, 5f), p.GetResource(res) / maxRes, Ui.ResourceColor(res));
            if (p.Pending != null && p.Pending.Ability != null) DrawCastBar(new Rect(cx, r.y + 29f, cw, 7f), p.Pending, true);

            if (HudDraw.Hover(r))
                Ui.TooltipFor(r, "<b>" + name + "</b>\nHealth " + labels.PetHealth.Get(Mathf.CeilToInt(p.Health), Mathf.RoundToInt(p.MaxHealth)) +
                                 (maxRes > 0f ? "\n" + HudText.ResourceName(res) + " " + Mathf.FloorToInt(p.GetResource(res)) + " / " + Mathf.RoundToInt(maxRes) : "") +
                                 (p.Lifetime > 0f ? "\n" + Ui.Rich(UiText.Duration(p.Lifetime) + " remaining", Hud.Muted) : "") +
                                 "\n" + Ui.Rich("Click to select (its abilities appear on the action bar).", Hud.Muted));
            Ui.Block(r);
            if (HudDraw.Click(r))
            {
                var unit = p;
                if (pick != null) Hud.ResolveFieldPick(unit);
                else Hud.ClickUnit(unit);
                Ui.Sfx?.Invoke("ui_click");
            }
            float y2 = DrawAuras(p, r.x + 2f, r.yMax + 2f, r.width - 4f);
            return Mathf.Max(r.yMax, y2);
        }

        static float DrawTotems(Unit u, float x, float y)
        {
            const float s = 24f;
            int i = 0;
            foreach (var kv in u.Totems)
            {
                var t = kv.Value;
                if (t == null || t.Dead) continue;
                var r = new Rect(x + i * (s + 4f), y, s, s);
                string element = kv.Key ?? "";
                string glyph = TotemGlyph(element);
                HudDraw.Icon(r, glyph, TotemColor(element));
                if (t.Lifetime > 0f) HudDraw.Text(new Rect(r.x - 4f, r.yMax - 11f, r.width + 8f, 12f), HudText.Duration(t.Lifetime), HudStyles.TinyCenter, Ui.TextLight);
                if (HudDraw.Hover(r))
                    Ui.TooltipFor(r, "<b>" + Hud.NameOf(t) + "</b>\n" + element + " totem · Health " + Mathf.CeilToInt(t.Health) + " / " + Mathf.RoundToInt(t.MaxHealth) +
                                     (t.Lifetime > 0f ? "\n" + Ui.Rich(UiText.Duration(t.Lifetime) + " remaining", Hud.Muted) : ""));
                i++;
            }
            return i > 0 ? y + s + 2f : y;
        }

        static string TotemGlyph(string element)
        {
            switch ((element ?? "").ToLowerInvariant())
            {
                case "earth": return "glyph_totem_earth";
                case "fire": return "glyph_totem_fire";
                case "water": return "glyph_totem_water";
                case "air": return "glyph_totem_air";
                default: return "glyph_totem";
            }
        }

        static Color TotemColor(string element)
        {
            switch ((element ?? "").ToLowerInvariant())
            {
                case "earth": return Hud.C("#b08a52");
                case "fire": return Hud.SchoolCol(School.Fire);
                case "water": return Hud.SchoolCol(School.Frost);
                case "air": return Hud.C("#d6e8f0");
                default: return Hud.SchoolCol(School.Nature);
            }
        }
    }
}
