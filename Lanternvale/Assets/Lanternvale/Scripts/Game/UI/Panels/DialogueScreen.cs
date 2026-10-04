// Dialogue window (state-driven: Session.Mode == Dialogue, Order 220, modal).
// Speaker portrait + name (NPC / companion / hero; the narrator has none), typewriter text (click or Space completes it,
// stable word-wrap: the unrevealed tail is drawn transparent), numbered choices (1–9) with coloured [TAG] prefixes, skill
// check previews ("Persuasion DC 12 · Seren +3 · 65%"), greyed previously-chosen lines and requirement-locked choices
// with their reason, Continue / End. Skill checks (SkillCheck events) play a d20 roll: the die tumbles and bounces, lands
// on the natural roll, the modifier counts up against the DC, then a success/failure flourish with sound; the next line
// waits for the roll. The roll overlay lingers if the conversation ends on it.
using System;
using System.Collections.Generic;
using System.Text;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using Lanternvale.World;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class DialogueScreen : IUiScreen
    {
        public string Id => "";
        public int Order => 220;

        public bool Visible => DialogueActive || roll.Active;
        public bool Modal => DialogueActive;

        static bool DialogueActive
        {
            get
            {
                var s = PanelKit.Sess;
                return s != null && GameFlow.HasGame && s.Mode == SessionMode.Dialogue && s.Dialogue != null && s.Dialogue.IsActive && s.Dialogue.Current != null;
            }
        }

        // ------------------------------------------------------------------ state

        DialogueView shown;
        string shownNode = "";
        float revealStart;
        int revealLen;          // visible characters of the current text
        bool revealAll;
        bool waitForRoll;
        int lockedUntilFrame;
        string speakerKey = "";
        float speakerSince;

        // cached per node
        string bodyText = "";
        string nameText = "", titleText = "", portraitKey = "";
        Color nameColor = Ui.Ink, frameColor = Ui.Gold;
        bool narrator;
        readonly List<ChoiceRow> rows = new List<ChoiceRow>();
        int cachedRevealN = -1;
        string cachedReveal = "";
        DialogueView measuredFor;
        float measuredW, measuredTextH, measuredChoicesH;

        sealed class ChoiceRow
        {
            public int Index;            // into Current.Choices, -1 = unavailable
            public string Text = "";
            public string Info = "";
            public bool Muted;
            public float Height;
            public string Number = "";
        }

        GUIStyle bodyStyle, narratorStyle, nameStyle, infoStyle, numberStyle, hintStyle;
        readonly RollAnim roll = new RollAnim();

        public DialogueScreen()
        {
            PanelKit.Events += OnEvent;
        }

        static DialogueScreen()
        {
            // UiRoot never opens the pause menu over a modal screen; a conversation is the exception (load / main
            // menu / settings stay reachable — saving is refused by the session while it runs)
            EscRouter.Register(200, () =>
            {
                if (!DialogueActive || UiRoot.IsOpen(UiPanels.Pause)) return false;
                UiRoot.Open(UiPanels.Pause);
                return true;
            });
        }

        void OnEvent(SessionEvent e)
        {
            if (e.Kind == SessionEventKind.SkillCheck && e.Check != null)
            {
                var s = PanelKit.Sess;
                if (s != null && s.Dialogue != null && s.Dialogue.IsActive) roll.Start(e.Check);
            }
            else if (e.Kind == SessionEventKind.GameStarted || e.Kind == SessionEventKind.GameLoaded)
            {
                roll.Stop();
                shown = null;
            }
        }

        void EnsureStyles()
        {
            if (bodyStyle != null && bodyStyle.font == Ui.BodyFont) return;
            bodyStyle = new GUIStyle(Ui.LabelDark) { fontSize = 22, wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
            bodyStyle.normal.textColor = Ui.Ink;
            bodyStyle.padding = new RectOffset(0, 0, 0, 0);
            narratorStyle = new GUIStyle(bodyStyle) { fontStyle = FontStyle.Italic };
            narratorStyle.normal.textColor = Ui.InkSoft;
            nameStyle = new GUIStyle(Ui.HeaderDark) { fontSize = 28, wordWrap = false, alignment = TextAnchor.MiddleLeft, richText = true };
            infoStyle = new GUIStyle(Ui.LabelDark) { fontSize = 16, wordWrap = false, richText = true, alignment = TextAnchor.MiddleLeft };
            infoStyle.normal.textColor = Ui.InkSoft;
            infoStyle.padding = new RectOffset(0, 0, 0, 0);
            numberStyle = new GUIStyle(Ui.LabelDark) { font = Ui.BoldFont, fontSize = 20, alignment = TextAnchor.UpperRight, wordWrap = false };
            numberStyle.normal.textColor = PanelKit.GoldInk;
            numberStyle.padding = new RectOffset(0, 0, 0, 0);
            hintStyle = new GUIStyle(Ui.LabelDark) { fontSize = 15, wordWrap = false, alignment = TextAnchor.MiddleRight };
            hintStyle.normal.textColor = new Color(Ui.InkSoft.r, Ui.InkSoft.g, Ui.InkSoft.b, 0.8f);
            hintStyle.padding = new RectOffset(0, 0, 0, 0);
        }

        // ------------------------------------------------------------------ tick (keys)

        public void Tick(float dt)
        {
            roll.Tick();
            if (!DialogueActive)
            {
                shown = null;
                return;
            }
            var s = PanelKit.Sess;
            var cur = s.Dialogue.Current;
            if (cur != shown || cur.NodeId != shownNode) OnNewView(s, cur);
            if (waitForRoll && !roll.Active)
            {
                waitForRoll = false;
                revealStart = Time.unscaledTime;
            }
            // keys belong to the pause menu / settings while one is open over the conversation (sheets left open
            // before it — bags, journal, combat log — are hidden or passive and must not swallow them)
            if (ConfirmScreen.IsOpen || EscRouter.OverlayPanelOpen() || Time.frameCount <= lockedUntilFrame) return;

            if (roll.Active)
            {
                if (GameInput.KeyDown(KeyCode.Space) || PanelKit.ConfirmKeyDown()) roll.Skip();
                return;
            }
            bool revealed = Revealed;
            if (GameInput.KeyDown(KeyCode.Space) || PanelKit.ConfirmKeyDown())
            {
                if (!revealed) revealAll = true;
                else if (cur.CanContinue) Continue();
            }
            int k = PanelKit.NumberKeyDown();
            if (k > 0)
            {
                if (!revealed) revealAll = true;
                else if (k - 1 < cur.Choices.Count) Choose(k - 1);
            }
        }

        bool Revealed => revealAll || PanelPrefs.TextSpeed <= 0f || VisibleChars() >= revealLen;

        int VisibleChars()
        {
            if (waitForRoll) return 0;
            float cps = 58f * Mathf.Max(0.05f, PanelPrefs.TextSpeed);
            return Mathf.FloorToInt((Time.unscaledTime - revealStart) * cps);
        }

        void Choose(int index)
        {
            lockedUntilFrame = Time.frameCount + 2;
            Ui.Sfx?.Invoke("ui_click");
            PanelKit.Do(() =>
            {
                var s = PanelKit.Sess;
                if (s != null && s.Mode == SessionMode.Dialogue) s.ChooseDialogue(index);
            });
        }

        void Continue()
        {
            lockedUntilFrame = Time.frameCount + 2;
            PanelKit.Do(() =>
            {
                var s = PanelKit.Sess;
                if (s == null || s.Mode != SessionMode.Dialogue || s.Dialogue.Current == null) return;
                if (s.Dialogue.Current.CanContinue) s.ContinueDialogue();
                else if (s.Dialogue.Current.Choices.Count == 0) s.EndDialogue();
            });
        }

        // ------------------------------------------------------------------ per-node cache

        void OnNewView(GameSession s, DialogueView v)
        {
            shown = v;
            shownNode = v.NodeId ?? "";
            revealStart = Time.unscaledTime;
            revealAll = false;
            cachedRevealN = -1;
            bodyText = v.Text ?? "";
            revealLen = bodyText.IndexOf('<') >= 0 ? 0 : bodyText.Length;
            if (revealLen == 0) revealAll = true;
            waitForRoll = roll.Active;   // the next line waits for the die

            ResolveSpeaker(s, v);
            BuildRows(s, v);
        }

        void ResolveSpeaker(GameSession s, DialogueView v)
        {
            var db = PanelKit.Db;
            string id = v.SpeakerId ?? "";
            nameText = v.SpeakerName ?? "";
            titleText = "";
            portraitKey = v.Portrait ?? "";
            narrator = string.IsNullOrEmpty(nameText) || id == "narrator";
            nameColor = Ui.Ink;
            frameColor = Ui.GoldDeep;
            Unit member = null;
            if (id == "player" || id == GameSession.MainId) member = s.Main;
            else if (!string.IsNullOrEmpty(id)) member = s.FindMember(id);
            if (member != null)
            {
                if (string.IsNullOrEmpty(portraitKey)) portraitKey = PanelKit.PortraitOf(member);
                if (member.Class != null)
                {
                    nameColor = PanelKit.InkColorOf(member.Class.id);
                    frameColor = Ui.ClassColor(member.Class.id);
                    titleText = member == s.Main ? "" : member.Class.name;
                }
            }
            else if (db != null && !string.IsNullOrEmpty(id))
            {
                if (db.Npcs.TryGetValue(id, out var npc) && npc != null)
                {
                    if (string.IsNullOrEmpty(portraitKey)) portraitKey = npc.portrait;
                    titleText = npc.title ?? "";
                    nameColor = PanelKit.GoldInk;
                }
                else if (db.Companions.TryGetValue(id, out var comp) && comp != null)
                {
                    if (string.IsNullOrEmpty(portraitKey)) portraitKey = comp.portrait;
                    titleText = comp.title ?? "";
                    nameColor = PanelKit.InkColorOf(comp.classId);
                    frameColor = Ui.ClassColor(comp.classId);
                }
                else
                {
                    var cr = db.Creature(id);
                    if (cr != null && string.IsNullOrEmpty(portraitKey)) portraitKey = !string.IsNullOrEmpty(cr.portrait) ? cr.portrait : cr.sprite;
                }
            }
            string key = narrator ? "" : (id + "|" + nameText);
            if (key != speakerKey) { speakerKey = key; speakerSince = Time.unscaledTime; }
        }

        void BuildRows(GameSession s, DialogueView v)
        {
            rows.Clear();
            for (int i = 0; i < v.Choices.Count; i++)
            {
                var c = v.Choices[i];
                var row = new ChoiceRow { Index = i, Number = (i + 1) + ".", Muted = c.PreviouslyChosen };
                row.Text = ChoiceText(c.Tag, c.Text, c.PreviouslyChosen, c.Ends);
                if (c.Check != null)
                {
                    var p = c.Check;
                    int pct = Mathf.RoundToInt(Mathf.Clamp01(p.SuccessChance) * 100f);
                    string who = string.IsNullOrEmpty(p.RollerName) ? "" : $" · {p.RollerName} {p.Modifier:+0;-0}";
                    row.Info = $"{PanelKit.SkillName(p.Skill)} DC {p.Dc}{who} · {pct}% chance";
                }
                rows.Add(row);
            }
            // choices hidden only by a requirement the party could still meet: shown locked with the reason
            try { AddLockedRows(s, v); }
            catch (Exception e) { Debug.LogWarning("[Lanternvale] Dialogue locked choices: " + e.Message); }
        }

        void AddLockedRows(GameSession s, DialogueView v)
        {
            var node = s.Dialogue.CurrentNode;
            var dlg = s.Dialogue.Dialogue;
            if (node == null || node.choices == null || dlg == null || node.id != v.NodeId) return;
            for (int i = 0; i < node.choices.Count; i++)
            {
                bool visible = false;
                foreach (var c in v.Choices) if (c.SourceIndex == i) { visible = true; break; }
                if (visible) continue;
                var ch = node.choices[i];
                if (ch == null || ch.conditions == null || ch.conditions.Count == 0) continue;
                if (ch.once && s.Dialogue.Memory != null && s.Dialogue.Memory.HasChosen(dlg.id, node.id, i)) continue;
                var reasons = new List<string>();
                bool story = false;
                foreach (var cond in ch.conditions)
                {
                    if (cond == null) continue;
                    bool ok;
                    try { ok = WorldRules.Check(cond, s); }
                    catch (Exception) { ok = true; }
                    if (ok) continue;
                    var why = RequirementText(s, cond);
                    if (why == null) { story = true; break; }
                    reasons.Add(why);
                }
                if (story || reasons.Count == 0) continue;
                string tag = !string.IsNullOrEmpty(ch.tag) ? ch.tag.Trim('[', ']').ToUpperInvariant() : (ch.check != null ? ch.check.skill.ToString().ToUpperInvariant() : "");
                string text = s.Dialogue.Substitute(ch.text ?? "");
                rows.Add(new ChoiceRow
                {
                    Index = -1,
                    Number = "",
                    Muted = true,
                    Text = ChoiceText(tag, text, true, false),
                    Info = "Requires " + string.Join(", ", reasons),
                });
            }
        }

        static string RequirementText(GameSession s, ConditionDef c)
        {
            var db = PanelKit.Db;
            switch (c.type)
            {
                case ConditionType.Gold: return Inventory.FormatMoney(c.amount);
                case ConditionType.HasItem:
                    var it = db != null ? db.Item(c.key) : null;
                    int n = Math.Max(1, c.amount);
                    return (n > 1 ? n + "× " : "") + (it != null ? it.name : c.key);
                case ConditionType.Level: return "level " + c.amount;
                case ConditionType.Companion: return $"{s.NpcName(c.key)}'s approval of {c.amount}";
                case ConditionType.InParty: return s.NpcName(c.key) + " in the party";
                default: return null;
            }
        }

        static string ChoiceText(string tag, string text, bool muted, bool ends)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(tag))
            {
                var col = PanelKit.TagColor(tag, out bool isClass);
                string label = tag;
                if (Enum.TryParse(tag, true, out SkillCheck sk)) label = PanelKit.SkillName(sk).ToUpperInvariant();
                if (muted) col = Color.Lerp(col, Ui.InkSoft, 0.5f);
                sb.Append("<b>").Append(Ui.Rich("[" + label + "]", col)).Append("</b> ");
            }
            string body = string.IsNullOrEmpty(text) ? "…" : text;
            sb.Append(muted ? Ui.Rich(body, Ui.InkSoft) : body);
            if (ends && !muted) sb.Append(Ui.Rich("  (leave)", Ui.InkSoft));
            return sb.ToString();
        }

        string RevealedText()
        {
            if (Revealed) return bodyText;
            int n = Mathf.Clamp(VisibleChars(), 0, revealLen);
            if (n == cachedRevealN) return cachedReveal;
            cachedRevealN = n;
            cachedReveal = n <= 0 ? "<color=#00000000>" + bodyText + "</color>" : bodyText.Substring(0, n) + "<color=#00000000>" + bodyText.Substring(n) + "</color>";
            return cachedReveal;
        }

        // ------------------------------------------------------------------ drawing

        public void Draw()
        {
            PanelKit.EnsureStyles();
            EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                if (DialogueActive && shown != null)
                {
                    PanelKit.OccludeAll(Order);
                    Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
                    DrawBox();
                }
                if (roll.Active) roll.Draw(Order, !DialogueActive);
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        void DrawBox()
        {
            float W = Ui.Width, H = Ui.Height;
            // cinematic shading: soft top bar and a darker floor behind the box
            if (PanelKit.IsRepaint)
            {
                PanelKit.Tex(new Rect(0f, 0f, W, 90f), PanelArt.GradientDown, new Color(0.05f, 0.04f, 0.09f, 0.55f));
                PanelKit.Tex(new Rect(0f, H * 0.5f, W, H * 0.5f), PanelArt.GradientUp, new Color(0.05f, 0.04f, 0.09f, 0.55f));
            }
            float bw = Mathf.Min(1280f, W - 60f);
            const float portrait = 176f;
            bool hasPortrait = !narrator;
            float textX = hasPortrait ? 44f + portrait + 30f : 48f;
            float textW = bw - textX - 40f;
            bool revealed = Revealed && !roll.Active;
            var style = narrator ? narratorStyle : bodyStyle;
            // measure once per line / width (CalcHeight is not free)
            if (measuredFor != shown || Mathf.Abs(measuredW - textW) > 0.5f)
            {
                measuredFor = shown;
                measuredW = textW;
                measuredTextH = Mathf.Max(30f, PanelKit.TextHeight(bodyText.Length > 0 ? bodyText : " ", style, textW));
                measuredChoicesH = 0f;
                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    row.Height = PanelKit.TextHeight(row.Text, PanelKit.ChoiceText, textW - 40f) + (row.Info.Length > 0 ? 22f : 0f) + 12f;
                    measuredChoicesH += row.Height + 4f;
                }
            }
            float textH = measuredTextH, choicesH = measuredChoicesH;
            bool canContinue = shown.CanContinue;
            float footer = canContinue ? 62f : 30f;
            float nameH = narrator ? 10f : 46f;
            float contentH = 26f + nameH + textH + (rows.Count > 0 ? 18f + choicesH : 0f) + footer;
            float bh = Mathf.Clamp(Mathf.Max(contentH, hasPortrait ? portrait + 70f : 160f), 160f, H - 120f);
            var box = new Rect((W - bw) * 0.5f, H - bh - 24f, bw, bh);
            Ui.Panel(box);

            // speaker
            if (hasPortrait)
            {
                float a = Mathf.Clamp01((Time.unscaledTime - speakerSince) / 0.25f);
                var pr = new Rect(box.x + 34f, box.y + 30f - (1f - a) * 8f, portrait, portrait);
                PanelKit.Tex(new Rect(pr.x - 24f, pr.y - 24f, pr.width + 48f, pr.height + 48f), ProceduralArt.Glow, new Color(frameColor.r, frameColor.g, frameColor.b, 0.45f * a));
                var oldC = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                Ui.Portrait(pr, portraitKey, frameColor);
                GUI.color = oldC;
            }
            float y = box.y + 26f;
            if (!narrator)
            {
                PanelKit.Label(new Rect(box.x + textX, y, textW, 38f), nameText, nameStyle, nameColor);
                if (titleText.Length > 0)
                {
                    float nw = PanelKit.TextWidth(nameText, nameStyle);
                    PanelKit.Label(new Rect(box.x + textX + nw + 12f, y + 6f, textW - nw - 12f, 30f), titleText, PanelKit.TextMuted);
                }
                y += nameH;
            }
            else y += nameH;

            // body text (typewriter) — while a die rolls the new line waits
            var tr = new Rect(box.x + textX, y, textW, textH + 4f);
            if (roll.Active)
                PanelKit.Label(tr, roll.Caption, narratorStyle);
            else
                GUI.Label(tr, RevealedText(), style);
            y += textH + 18f;

            // choices
            if (rows.Count > 0)
            {
                if (revealed)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var rr = new Rect(box.x + textX - 12f, y, textW + 24f, row.Height);
                        DrawChoice(rr, row);
                        y += row.Height + 4f;
                    }
                }
                else y += choicesH;
            }

            // footer
            if (canContinue && revealed)
            {
                string label = shown.IsLast ? "End conversation" : "Continue";
                var br = new Rect(box.xMax - 250f, box.yMax - 62f, 220f, 46f);
                if (Ui.Btn(br, label, Ui.ButtonGold)) Continue();
                PanelKit.Label(new Rect(box.xMax - 600f, br.y, 336f, br.height), "Space", hintStyle);
            }
            else if (!revealed && !roll.Active)
                PanelKit.Label(new Rect(box.xMax - 440f, box.yMax - 40f, 400f, 26f), "click or Space to finish the line", hintStyle);
            else if (rows.Count > 0)
                PanelKit.Label(new Rect(box.xMax - 440f, box.yMax - 36f, 400f, 26f), "1–9 or click to choose", hintStyle);

            // a click anywhere on the box completes the line / continues
            if (PanelKit.LeftClick(box) && Time.frameCount > lockedUntilFrame)
            {
                if (roll.Active) roll.Skip();
                else if (!Revealed) revealAll = true;
                else if (canContinue) Continue();
            }
        }

        void DrawChoice(Rect rr, ChoiceRow row)
        {
            bool enabled = row.Index >= 0 && !roll.Active;
            bool hover = enabled && PanelKit.Hover(rr);
            if (hover) PanelKit.Rounded(rr, PanelKit.RowHover);
            var numR = new Rect(rr.x, rr.y + 6f, 34f, 26f);
            if (row.Index >= 0) PanelKit.Label(numR, row.Number, numberStyle, row.Muted ? Ui.InkSoft : PanelKit.GoldInk);
            else PanelKit.Tex(new Rect(numR.x + 12f, numR.y + 2f, 20f, 20f), PanelArt.Lock, new Color(Ui.InkSoft.r, Ui.InkSoft.g, Ui.InkSoft.b, 0.7f), ScaleMode.ScaleToFit);
            float th = row.Height - 12f - (row.Info.Length > 0 ? 22f : 0f);
            GUI.Label(new Rect(rr.x + 46f, rr.y + 6f, rr.width - 56f, th + 2f), row.Text, PanelKit.ChoiceText);
            if (row.Info.Length > 0)
                GUI.Label(new Rect(rr.x + 46f, rr.y + 6f + th, rr.width - 56f, 22f), row.Info, infoStyle);
            if (enabled && PanelKit.LeftClick(rr) && Time.frameCount > lockedUntilFrame) Choose(row.Index);
        }

        // ================================================================== d20 roll

        sealed class RollAnim
        {
            CheckResult check;
            float start;
            bool skipped;
            int lastFace = -1;
            float nextTick;
            int phase;   // 0 tumble, 1 landed, 2 total, 3 verdict
            string caption = "", breakdown = "", verdict = "", skillLine = "";
            readonly System.Random rng = new System.Random();
            int face = 1;
            GUIStyle bigNumber, verdictStyle, smallStyle;

            const float Tumble = 1.15f, Total = 1.75f, Verdict = 2.3f, Hold = 3.9f, Fade = 0.35f;

            public bool Active => check != null && Time.unscaledTime < start + Hold + Fade;
            public float EndsAt => start + Hold + Fade;
            public string Caption => caption;

            public void Start(CheckResult c)
            {
                check = c;
                start = Time.unscaledTime;
                skipped = false;
                phase = 0;
                lastFace = -1;
                nextTick = 0f;
                string roller = string.IsNullOrEmpty(c.RollerName) ? "The party" : c.RollerName;
                skillLine = $"{PanelKit.SkillName(c.Skill)} check · DC {c.Dc}";
                caption = $"<i>{roller} rolls for {PanelKit.SkillName(c.Skill)}…</i>";
                var sb = new StringBuilder();
                sb.Append("d20 ").Append(c.Roll);
                string stat = SkillChecks.StatFor(c.Skill).ToString();
                if (c.StatModifier != 0) sb.Append(c.StatModifier > 0 ? "  + " : "  − ").Append(Math.Abs(c.StatModifier)).Append(' ').Append(stat);
                if (c.Proficiency != 0) sb.Append("  + ").Append(c.Proficiency).Append(" proficiency");
                if (c.Bonus != 0) sb.Append(c.Bonus > 0 ? "  + " : "  − ").Append(Math.Abs(c.Bonus)).Append(" bonus");
                sb.Append("  =  ").Append(c.Total).Append("   vs DC ").Append(c.Dc);
                breakdown = sb.ToString();
                verdict = c.Critical ? "CRITICAL SUCCESS!" : c.Fumble ? "CRITICAL FAILURE" : c.Success ? "SUCCESS" : "FAILURE";
                Ui.Sfx?.Invoke("ui_open");
            }

            public void Stop() => check = null;

            /// <summary>Space/click: jump to the verdict, or dismiss once it is shown.</summary>
            public void Skip()
            {
                if (check == null) return;
                float t = Time.unscaledTime - start;
                if (t < Verdict) { start = Time.unscaledTime - Verdict; skipped = true; }
                else if (t < Hold) start = Time.unscaledTime - Hold;
            }

            public void Tick()
            {
                if (check == null) return;
                float t = Time.unscaledTime - start;
                if (t > Hold + Fade) { check = null; return; }
                if (t < Tumble)
                {
                    // faces flicker, slowing down; soft ticks
                    float interval = Mathf.Lerp(0.05f, 0.16f, t / Tumble);
                    if (Time.unscaledTime >= nextTick)
                    {
                        nextTick = Time.unscaledTime + interval;
                        int f;
                        do { f = rng.Next(1, 21); } while (f == face);
                        face = f;
                        Sfx.Play("ui_click", null, 0.35f, 0.9f + (float)rng.NextDouble() * 0.5f);
                    }
                    return;
                }
                face = check.Roll;
                if (phase < 1)
                {
                    phase = 1;
                    Sfx.Play("hit_physical", null, 0.55f, 0.75f);
                }
                if (t >= Total && phase < 2) phase = 2;
                if (t >= Verdict && phase < 3)
                {
                    phase = 3;
                    if (check.Critical) Sfx.Play("level_up", null, 0.9f, 1.05f);
                    else if (check.Success) Sfx.Play("buff", null, 0.9f, 1f);
                    else Sfx.Play("debuff", null, 0.9f, check.Fumble ? 0.8f : 1f);
                }
            }

            void Styles()
            {
                if (bigNumber != null && bigNumber.font == Ui.TitleFont) return;
                bigNumber = new GUIStyle(Ui.Title) { fontSize = 64, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                bigNumber.normal.textColor = Ui.Ink;
                verdictStyle = new GUIStyle(Ui.Title) { fontSize = 46, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                smallStyle = new GUIStyle(Ui.Label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = true };
            }

            public void Draw(int order, bool standalone)
            {
                if (check == null) return;
                Styles();
                float t = Time.unscaledTime - start;
                float alpha = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((Hold + Fade - t) / Fade);
                float W = Ui.Width, H = Ui.Height;
                var c = new Vector2(W * 0.5f, H * 0.36f);
                var plate = new Rect(c.x - 330f, c.y - 190f, 660f, 380f);
                PanelKit.Occlude(plate, order);
                Ui.Block(plate);
                var oldColor = GUI.color;
                var oldMatrix = GUI.matrix;
                try
                {
                    // backdrop glow
                    PanelKit.Tex(new Rect(c.x - 380f, c.y - 260f, 760f, 520f), ProceduralArt.Glow, new Color(0.05f, 0.03f, 0.10f, 0.75f * alpha));
                    PanelKit.Label(new Rect(plate.x, plate.y + 14f, plate.width, 34f), skillLine, CenterHeading(), new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, alpha));

                    // the die: bounces, spins and settles
                    float size = 150f;
                    float k = Mathf.Clamp01(t / Tumble);
                    float bounce = t < Tumble ? Mathf.Abs(Mathf.Sin(k * Mathf.PI * 3.2f)) * (1f - k) * 70f : 0f;
                    float spin = t < Tumble ? (1f - k) * (1f - k) * 900f + Mathf.Sin(t * 30f) * 8f * (1f - k) : 0f;
                    float pop = t >= Tumble && t < Tumble + 0.25f ? 1f + 0.18f * Mathf.Sin((t - Tumble) / 0.25f * Mathf.PI) : 1f;
                    var dieCenter = new Vector2(c.x, c.y - 10f - bounce);
                    var dieRect = new Rect(dieCenter.x - size * 0.5f * pop, dieCenter.y - size * 0.5f * pop, size * pop, size * pop);
                    if (phase >= 3)
                    {
                        var glowCol = check.Success ? new Color(1f, 0.85f, 0.4f, 0.9f * alpha) : new Color(0.55f, 0.45f, 0.75f, 0.7f * alpha);
                        PanelKit.Tex(new Rect(dieRect.x - 50f, dieRect.y - 50f, dieRect.width + 100f, dieRect.height + 100f), ProceduralArt.Glow, glowCol);
                    }
                    PanelKit.Tex(new Rect(dieCenter.x - 60f, c.y + 72f, 120f, 18f), PanelArt.Disc, new Color(0f, 0f, 0f, 0.25f * alpha));
                    if (spin != 0f) { GUIUtility.RotateAroundPivot(spin, dieCenter * Ui.Scale); PanelKit.Rehide(); }
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    if (PanelKit.IsRepaint) GUI.DrawTexture(dieRect, PanelArt.D20, ScaleMode.ScaleToFit);
                    var numCol = phase >= 1 && check.Critical ? PanelKit.GoodDark : phase >= 1 && check.Fumble ? PanelKit.BadDark : Ui.Ink;
                    PanelKit.Label(new Rect(dieRect.x, dieRect.y + dieRect.height * 0.06f, dieRect.width, dieRect.height), PanelKit.CountText(face),
                        bigNumber, new Color(numCol.r, numCol.g, numCol.b, alpha));
                    PanelKit.SetMatrix(oldMatrix);

                    // total and verdict
                    if (phase >= 2)
                    {
                        float a2 = Mathf.Clamp01((t - Total) / 0.25f) * alpha;
                        PanelKit.Label(new Rect(plate.x, c.y + 96f, plate.width, 30f), breakdown, smallStyle, new Color(1f, 0.96f, 0.88f, a2));
                    }
                    if (phase >= 3)
                    {
                        float vt = (t - Verdict) / 0.3f;
                        float a3 = Mathf.Clamp01(vt) * alpha;
                        float s = 1f + 0.35f * Mathf.Clamp01(1f - vt);
                        var vc = check.Success ? Ui.Hex("#ffd86b") : Ui.Hex("#c9b6e8");
                        var vr = new Rect(plate.x, c.y + 130f, plate.width, 56f);
                        GUIUtility.ScaleAroundPivot(new Vector2(s, s), vr.center * Ui.Scale);
                        PanelKit.Rehide();
                        Ui.Shadowed(vr, verdict, verdictStyle, new Color(vc.r, vc.g, vc.b, a3));
                        PanelKit.SetMatrix(oldMatrix);
                        if (check.Success) DrawSparkles(dieCenter, t - Verdict, alpha);
                    }
                }
                finally
                {
                    if (GUI.matrix != oldMatrix) PanelKit.SetMatrix(oldMatrix);
                    GUI.color = oldColor;
                }
                if (PanelKit.LeftClick(plate)) Skip();
            }

            GUIStyle centerHeading;

            GUIStyle CenterHeading()
            {
                if (centerHeading == null) centerHeading = new GUIStyle(PanelKit.LHeading) { alignment = TextAnchor.MiddleCenter };
                return centerHeading;
            }

            static void DrawSparkles(Vector2 c, float t, float alpha)
            {
                if (!PanelKit.IsRepaint) return;
                const int n = 14;
                for (int i = 0; i < n; i++)
                {
                    float ang = i * (360f / n) * Mathf.Deg2Rad + i * 0.37f;
                    float d = 60f + t * (180f + (i % 3) * 40f);
                    float a = Mathf.Clamp01(1f - t / 1.3f) * alpha;
                    if (a <= 0f) continue;
                    float s = 18f + (i % 4) * 6f;
                    var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d;
                    PanelKit.Tex(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), PanelArt.Sparkle, new Color(1f, 0.88f, 0.5f, a));
                }
            }
        }
    }
}
