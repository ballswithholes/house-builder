// Character creation (full-screen, Order 310; GameRoot.Mode == CharacterCreation while no game runs).
// Name field (hotkeys suppressed while typing), the 8 class cards (crest, class colour, roles, resource), the hero art,
// class details (description, resource, armour & weapons, design notes, abilities at the chosen level with tooltips),
// start level (1 … 60, "veteran start"), veteran options, Back / Begin → GameFlow.StartNewGame(NewGameOptions).
// Keyboard: Left/Right (A/D) or Up/Down choose the class while not typing, Enter begins, Esc goes back.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Session;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class CharacterCreationScreen : IUiScreen
    {
        public string Id => "";
        public int Order => 310;
        public bool Modal => true;

        public bool Visible
        {
            get
            {
                var root = GameRoot.Instance;
                return root != null && GameFlow.Instance != null && !GameFlow.HasGame && root.Mode == GameMode.CharacterCreation;
            }
        }

        static CharacterCreationScreen instance;
        const string NameControl = "lv_charname";

        static readonly ClassId[] ClassOrder =
        {
            ClassId.Warrior, ClassId.Paladin, ClassId.Hunter, ClassId.Rogue,
            ClassId.Priest, ClassId.Shaman, ClassId.Mage, ClassId.Warlock,
        };

        static readonly int[] Levels = { 1, 10, 20, 30, 40, 50, 60 };
        static readonly string[] LevelLabels = { "1", "10", "20", "30", "40", "50", "60" };

        static readonly string[] Names =
        {
            "Wren", "Ember", "Juniper", "Rowan", "Sable", "Fennel", "Isolde", "Corin", "Linnea", "Tobin", "Maelis", "Saffi",
            "Orrin", "Thistle", "Yuki", "Haru", "Kaito", "Mirelle", "Odo", "Aster", "Briar", "Calla", "Daro", "Elowen",
        };

        int classIndex;
        int captionKey = -1;
        string caption = "", levelHeading = "Starting out";
        int levelIndex;
        string heroName = "";
        bool veteranGear = true, autoTalents = true;
        bool wasVisible, focused, focusFlagSet;
        float shownAt;
        string error = "";
        readonly System.Random rng = new System.Random();
        readonly PanelKit.ScrollState infoScroll = new PanelKit.ScrollState();

        // per class+level caches
        sealed class Preview
        {
            public Unit Unit;
            public readonly List<AbilityDef> Abilities = new List<AbilityDef>();
            public string Stats = "";
        }

        readonly Dictionary<int, Preview> previews = new Dictionary<int, Preview>();
        readonly Dictionary<ClassId, string[]> classText = new Dictionary<ClassId, string[]>();

        public CharacterCreationScreen()
        {
            instance = this;
        }

        static CharacterCreationScreen()
        {
            EscRouter.Register(110, () =>
            {
                var c = instance;
                if (c == null || !c.Visible) return false;
                if (c.focused) { c.Unfocus(); return true; }
                c.Back();
                return true;
            });
        }

        ClassId Current => ClassOrder[Mathf.Clamp(classIndex, 0, ClassOrder.Length - 1)];
        int StartLevel => Levels[Mathf.Clamp(levelIndex, 0, Levels.Length - 1)];

        bool wantUnfocus;

        /// <summary>Drops keyboard focus from the name field (applied in OnGUI: GUI calls are illegal in Update).</summary>
        void Unfocus()
        {
            wantUnfocus = true;
            focused = false;
            if (focusFlagSet) { GameInput.SetTextFieldFocused(false); focusFlagSet = false; }
        }

        void Back()
        {
            Unfocus();
            Ui.Sfx?.Invoke("ui_close");
            PanelKit.Do(() => { if (GameRoot.Instance != null && !GameFlow.HasGame) GameRoot.Instance.SetMode(GameMode.MainMenu); });
        }

        public void Tick(float dt)
        {
            bool vis = Visible;
            if (vis && !wasVisible)
            {
                shownAt = Time.unscaledTime;
                error = "";
                if (string.IsNullOrEmpty(heroName)) heroName = RandomName();
            }
            if (!vis && focusFlagSet)
            {
                GameInput.SetTextFieldFocused(false);
                focusFlagSet = false;
                focused = false;
            }
            wasVisible = vis;
            if (!vis) return;
            if (focused)
            {
                UiRoot.HotkeysSuppressed = true;
                return;
            }
            if (ConfirmScreen.IsOpen) return;
            int d = 0;
            if (GameInput.KeyDown(KeyCode.RightArrow) || GameInput.KeyDown(KeyCode.D) || GameInput.KeyDown(KeyCode.DownArrow) || GameInput.KeyDown(KeyCode.S)) d = 1;
            if (GameInput.KeyDown(KeyCode.LeftArrow) || GameInput.KeyDown(KeyCode.A) || GameInput.KeyDown(KeyCode.UpArrow) || GameInput.KeyDown(KeyCode.W)) d = -1;
            if (d != 0)
            {
                classIndex = (classIndex + d + ClassOrder.Length) % ClassOrder.Length;
                infoScroll.Reset();
                Ui.Sfx?.Invoke("ui_click");
            }
            if (PanelKit.ConfirmKeyDown()) Begin();
        }

        string RandomName() => Names[rng.Next(Names.Length)];

        void Begin()
        {
            var name = (heroName ?? "").Trim();
            if (name.Length == 0) name = heroName = RandomName();
            var opts = new NewGameOptions
            {
                Name = name,
                Class = Current,
                StartLevel = StartLevel,
                VeteranGear = veteranGear,
                AutoAllocateTalents = autoTalents,
                PlayOpening = true,
            };
            Unfocus();
            Ui.Sfx?.Invoke("level_up");
            PanelKit.Do(() =>
            {
                var f = GameFlow.Instance;
                if (f == null) return;
                f.StartNewGame(opts);
                if (!string.IsNullOrEmpty(f.LastError)) { error = f.LastError; PanelKit.Notice(f.LastError); }
            });
        }

        Preview PreviewOf(ClassId c, int level)
        {
            int key = (int)c * 100 + level;
            if (previews.TryGetValue(key, out var p)) return p;
            p = new Preview();
            previews[key] = p;
            var db = PanelKit.Db;
            if (db == null || db.Class(c) == null) return p;
            try
            {
                p.Unit = UnitFactory.CreateCharacter(db, c, "Preview", level, level > 1);
                foreach (var kv in p.Unit.Abilities)
                {
                    var a = db.Ability(kv.Key);
                    if (a == null || a.hidden || a.classId != c) continue;
                    p.Abilities.Add(a);
                }
                p.Abilities.Sort((x, y) => x.passive != y.passive ? (x.passive ? 1 : -1) : x.learnLevel != y.learnLevel ? x.learnLevel.CompareTo(y.learnLevel) : string.CompareOrdinal(x.name, y.name));
                var u = p.Unit;
                string res = u.PowerType == ResourceType.Mana ? $"   ·   Mana {Mathf.RoundToInt(u.MaxMana)}" : u.PowerType != ResourceType.None ? $"   ·   {PanelKit.ResourceName(u.PowerType)} 100" : "";
                var st = u.Stats;
                p.Stats = $"Health {Mathf.RoundToInt(u.MaxHealth)}{res}\n" +
                          $"Str {Mathf.RoundToInt(st.Strength)}  ·  Agi {Mathf.RoundToInt(st.Agility)}  ·  Sta {Mathf.RoundToInt(st.Stamina)}  ·  Int {Mathf.RoundToInt(st.Intellect)}  ·  Spi {Mathf.RoundToInt(st.Spirit)}";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Lanternvale] Class preview failed: " + e.Message);
            }
            return p;
        }

        string[] TextOf(ClassId c)
        {
            if (classText.TryGetValue(c, out var t)) return t;
            t = new string[6];
            var db = PanelKit.Db;
            var def = db != null ? db.Class(c) : null;
            if (def == null) { for (int i = 0; i < t.Length; i++) t[i] = ""; classText[c] = t; return t; }
            t[0] = def.roles != null && def.roles.Length > 0 ? string.Join(" · ", def.roles) : "";
            t[1] = ResourceLine(def);
            t[2] = ArmorLine(def);
            t[3] = WeaponLine(def);
            t[4] = def.description ?? "";
            t[5] = def.designNotes ?? "";
            classText[c] = t;
            return t;
        }

        static string ResourceLine(ClassDef d)
        {
            switch (d.id)
            {
                case ClassId.Warrior: return Ui.Rich("Rage", Ui.Rage) + " — builds as you deal and take damage, fades out of combat. Stances shape your role.";
                case ClassId.Rogue: return Ui.Rich("Energy", PanelKit.GoldInk) + " (refills every turn) and " + Ui.Rich("combo points", BadInk) + " — builders add points, finishers spend them.";
                case ClassId.Hunter: return Ui.Rich("Mana", Ui.Mana) + " for shots and stings; your pet fights with " + Ui.Rich("Focus", Ui.Focus) + ". Keep out of the dead zone.";
                case ClassId.Warlock: return Ui.Rich("Mana", Ui.Mana) + " (the five-second rule) plus " + Ui.Rich("soul shards", Ui.Hex("#8a35c9")) + " for summons and stones. Life Tap trades health for mana.";
                case ClassId.Shaman: return Ui.Rich("Mana", Ui.Mana) + " (the five-second rule). One totem per element: earth, fire, water and air.";
                case ClassId.Paladin: return Ui.Rich("Mana", Ui.Mana) + " (the five-second rule). Seals, judgements, auras and blessings.";
                default: return Ui.Rich("Mana", Ui.Mana) + " — regenerates from Spirit, slowly while you keep casting (the five-second rule).";
            }
        }

        static readonly Color BadInk = Ui.Hex("#c0392b");

        static string ArmorLine(ClassDef d)
        {
            var best = ArmorType.None;
            if (d.armorTypes != null) foreach (var a in d.armorTypes) if (a > best) best = a;
            string s = best == ArmorType.None ? "None" : best.ToString();
            if (d.armorUpgrade != null && d.armorUpgrade.type > best) s += $" ({d.armorUpgrade.type} at level {d.armorUpgrade.level})";
            if (d.canBlock) s += ", shields";
            return s;
        }

        static string WeaponLine(ClassDef d)
        {
            if (d.weaponTypes == null || d.weaponTypes.Length == 0) return "—";
            var parts = new List<string>();
            foreach (var w in d.weaponTypes)
            {
                if (w == WeaponType.Shield) continue;
                parts.Add(WeaponName(w));
            }
            string s = string.Join(", ", parts);
            if (d.dualWieldLevel > 0) s += d.dualWieldLevel <= 1 ? "; dual wield" : $"; dual wield at {d.dualWieldLevel}";
            return s;
        }

        static string WeaponName(WeaponType w)
        {
            switch (w)
            {
                case WeaponType.OneHandSword: return "swords";
                case WeaponType.TwoHandSword: return "greatswords";
                case WeaponType.OneHandAxe: return "axes";
                case WeaponType.TwoHandAxe: return "greataxes";
                case WeaponType.OneHandMace: return "maces";
                case WeaponType.TwoHandMace: return "great maces";
                case WeaponType.FistWeapon: return "fist weapons";
                case WeaponType.HeldInOffhand: return "off-hand relics";
                case WeaponType.Staff: return "staves";
                default: return UiText.Spaced(w.ToString()).ToLowerInvariant() + "s";
            }
        }

        // ------------------------------------------------------------------ drawing

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            var oldMatrix = GUI.matrix;
            try
            {
                PanelKit.OccludeAll(Order);
                Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
                if (PanelKit.IsRepaint)
                {
                    PanelKit.Rect(new Rect(0f, 0f, Ui.Width, Ui.Height), new Color(0.09f, 0.07f, 0.14f, 0.55f));
                    var vig = ArtLibrary.HasRealTexture("ui_vignette") ? ArtLibrary.Texture("ui_vignette") : null;
                    if (vig != null) PanelKit.Tex(new Rect(0f, 0f, Ui.Width, Ui.Height), vig, Color.white);
                }
                PanelKit.BeginCanvas(1920f, 1080f, out var canvas);
                float fade = Mathf.Clamp01((Time.unscaledTime - shownAt) / 0.5f);
                var oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, fade);

                // the name field is drawn first so its control id never shifts
                DrawNameField(new Rect(60f, 884f, 420f, 50f));

                Ui.Shadowed(new Rect(60f, 22f, 900f, 64f), "Create your hero", LeftTitle());
                PanelKit.Label(new Rect(64f, 80f, 900f, 28f), "Eight classes that play like their World of Warcraft Classic counterparts — turned into six-second turns.", PanelKit.LTextSmall, new Color(1f, 0.94f, 0.85f, 0.8f));

                DrawClassCards(new Rect(60f, 126f, 500f, 700f));
                DrawHero(new Rect(600f, 126f, 470f, 700f));
                DrawInfo(new Rect(1110f, 126f, 750f, 700f));
                DrawBottom(new Rect(60f, 846f, 1800f, 210f));
                GUI.color = oldColor;
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally
            {
                PanelKit.EndCanvas(oldMatrix);
                PanelKit.EndLayer(layer);
            }
        }

        GUIStyle leftTitle;

        GUIStyle LeftTitle()
        {
            if (leftTitle == null) leftTitle = new GUIStyle(PanelKit.LTitle) { alignment = TextAnchor.MiddleLeft };
            return leftTitle;
        }

        void DrawNameField(Rect r)
        {
            GUI.SetNextControlName(NameControl);
            var e = Event.current;
            bool leaveNow = focused && e.type == EventType.KeyDown &&
                            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab);
            var n = GUI.TextField(r, heroName ?? "", 18, Ui.TextField);
            if (n != heroName) heroName = n;
            if (leaveNow) { wantUnfocus = true; e.Use(); }
            if (wantUnfocus)
            {
                wantUnfocus = false;
                if (GUI.GetNameOfFocusedControl() == NameControl) GUI.FocusControl(null);
            }
            bool f = GUI.GetNameOfFocusedControl() == NameControl;
            if (f != focused || f != focusFlagSet)
            {
                focused = f;
                focusFlagSet = f;
                GameInput.SetTextFieldFocused(f);
            }
            if (f) UiRoot.HotkeysSuppressed = true;
            GameInput.BlockRectGui(r);
        }

        void DrawClassCards(Rect area)
        {
            var db = PanelKit.Db;
            const float cw = 244f, ch = 160f, gap = 12f;
            for (int i = 0; i < ClassOrder.Length; i++)
            {
                var c = ClassOrder[i];
                var def = db != null ? db.Class(c) : null;
                int col = i % 2, row = i / 2;
                var r = new Rect(area.x + col * (cw + gap), area.y + row * (ch + gap), cw, ch);
                bool sel = i == classIndex;
                bool hover = PanelKit.Hover(r);
                var cc = Ui.ClassColor(c);
                if (sel) PanelKit.Tex(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f), ProceduralArt.Glow, new Color(cc.r, cc.g, cc.b, 0.75f));
                Ui.Panel(r, Ui.InkPanel, false);
                if (hover && !sel) PanelKit.Rounded(r, new Color(1f, 1f, 1f, 0.06f));
                if (sel) PanelKit.Outline(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Ui.Gold);
                var crest = new Rect(r.x + 14f, r.y + 16f, 64f, 64f);
                var tex = ArtLibrary.HasRealTexture("crest_" + c.ToString().ToLowerInvariant()) ? ArtLibrary.Texture("crest_" + c.ToString().ToLowerInvariant()) : Ui.GlyphTexture(def != null ? def.icon : "");
                if (tex != null) PanelKit.Tex(crest, tex, Color.Lerp(cc, Color.white, 0.25f), ScaleMode.ScaleToFit);
                else PanelKit.Tex(crest, PanelArt.Disc, cc);
                PanelKit.Label(new Rect(r.x + 90f, r.y + 14f, r.width - 98f, 34f), def != null ? def.name : c.ToString(), PanelKit.LHeading, cc);
                var t = TextOf(c);
                PanelKit.Label(new Rect(r.x + 90f, r.y + 50f, r.width - 98f, 44f), t[0], PanelKit.LTextSmall, Ui.TextMuted);
                // resource pill
                var res = def != null ? def.resource : ResourceType.Mana;
                string rl = c == ClassId.Rogue ? "Energy · Combo" : c == ClassId.Warlock ? "Mana · Shards" : c == ClassId.Hunter ? "Mana · Pet" : PanelKit.ResourceName(res);
                var pill = new Rect(r.x + 14f, r.yMax - 44f, PanelKit.TextWidth(rl, PanelKit.LTextSmall) + 22f, 28f);
                PanelKit.Rounded(pill, new Color(Ui.ResourceColor(res).r, Ui.ResourceColor(res).g, Ui.ResourceColor(res).b, 0.32f));
                PanelKit.Label(new Rect(pill.x + 11f, pill.y + 2f, pill.width, 24f), rl, PanelKit.LTextSmall);
                GameInput.BlockRectGui(r);
                if (PanelKit.LeftClick(r) && i != classIndex)
                {
                    classIndex = i;
                    infoScroll.Reset();
                    Ui.Sfx?.Invoke("ui_click");
                }
            }
        }

        void DrawHero(Rect r)
        {
            var c = Current;
            var db = PanelKit.Db;
            var def = db != null ? db.Class(c) : null;
            var cc = Ui.ClassColor(c);
            Ui.Panel(r, Ui.PaperPanel);
            PanelKit.Tex(new Rect(r.x + 30f, r.y + 60f, r.width - 60f, r.height - 120f), ProceduralArt.Glow, new Color(cc.r, cc.g, cc.b, 0.55f));
            string sprite = def != null && !string.IsNullOrEmpty(def.sprite) ? def.sprite : "char_" + c.ToString().ToLowerInvariant();
            var tex = ArtLibrary.Texture(sprite);
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f) * 3f;
            var art = new Rect(r.x + 20f, r.y + 26f + bob, r.width - 40f, r.height - 70f);
            PanelKit.Tex(new Rect(r.x + r.width * 0.3f, r.yMax - 64f, r.width * 0.4f, 26f), PanelArt.Disc, new Color(0.1f, 0.07f, 0.15f, 0.25f));
            PanelKit.Tex(art, tex, Color.white, ScaleMode.ScaleToFit);
            PanelKit.Label(new Rect(r.x, r.yMax - 46f, r.width, 34f), def != null ? def.name : c.ToString(), PanelKit.HeadingCenter, PanelKit.InkColorOf(c));
        }

        void DrawInfo(Rect r)
        {
            var c = Current;
            var db = PanelKit.Db;
            var def = db != null ? db.Class(c) : null;
            Ui.Panel(r, Ui.PaperPanel);
            var inner = new Rect(r.x + 26f, r.y + 20f, r.width - 52f, r.height - 40f);
            var t = TextOf(c);
            var p = PreviewOf(c, StartLevel);
            float w = inner.width - 20f;
            // measure
            float hDesc = PanelKit.TextHeight(t[4], PanelKit.Text, w);
            float hNotes = PanelKit.TextHeight(t[5], PanelKit.TextSmall, w);
            float hRes = PanelKit.TextHeight(t[1], PanelKit.TextSmall, w - 110f);
            const float icon = 46f, igap = 8f;
            int perRow = Mathf.Max(1, (int)((w + igap) / (icon + igap)));
            int rows = Mathf.CeilToInt(p.Abilities.Count / (float)perRow);
            float content = 50f + 30f + hDesc + 14f + Mathf.Max(28f, hRes) + 8f + 28f + 28f + 14f + 36f + hNotes + 18f + 36f + 50f + rows * (icon + igap) + 20f;

            float cw = PanelKit.BeginScroll(inner, infoScroll, content);
            try
            {
                float y = 0f;
                PanelKit.Label(new Rect(0f, y, cw, 46f), def != null ? def.name : c.ToString(), PanelKit.TitleDark, PanelKit.InkColorOf(c));
                y += 48f;
                PanelKit.Label(new Rect(0f, y, cw, 26f), t[0], PanelKit.TextMuted);
                y += 30f;
                PanelKit.Label(new Rect(0f, y, cw, hDesc + 4f), t[4], PanelKit.Text);
                y += hDesc + 14f;
                Field(ref y, cw, "Resource", t[1], Mathf.Max(28f, hRes));
                Field(ref y, cw, "Armour", t[2], 28f);
                Field(ref y, cw, "Weapons", t[3], 28f);
                y += 14f;
                PanelKit.Label(new Rect(0f, y, cw, 32f), "How it plays", PanelKit.Heading);
                y += 36f;
                PanelKit.Label(new Rect(0f, y, cw, hNotes + 4f), t[5], PanelKit.TextSmall);
                y += hNotes + 18f;
                PanelKit.Label(new Rect(0f, y, cw, 32f), levelHeading, PanelKit.Heading);
                y += 36f;
                PanelKit.Label(new Rect(0f, y, cw, 48f), p.Stats, PanelKit.TextSmall);
                y += 50f;
                for (int i = 0; i < p.Abilities.Count; i++)
                {
                    var a = p.Abilities[i];
                    var ir = new Rect((i % perRow) * (icon + igap), y + (i / perRow) * (icon + igap), icon, icon);
                    if (!infoScroll.IsVisible(ir)) continue;
                    Ui.Icon(ir, a.icon, Ui.SchoolColor(a.school), 0f, a.passive);
                    if (a.passive && PanelKit.IsRepaint) PanelKit.Label(new Rect(ir.x, ir.yMax - 16f, ir.width, 14f), "passive", Ui.NumberStyle(10));
                    Ui.TooltipFor(ir, AbilityTip(p, a));
                }
            }
            finally { PanelKit.EndScroll(infoScroll); }
        }

        readonly Dictionary<AbilityDef, string> abilityTips = new Dictionary<AbilityDef, string>();
        Preview tipsFor;

        string AbilityTip(Preview p, AbilityDef a)
        {
            if (tipsFor != p) { abilityTips.Clear(); tipsFor = p; }
            if (abilityTips.TryGetValue(a, out var s)) return s;
            s = UiText.Ability(p.Unit, a);
            abilityTips[a] = s;
            return s;
        }

        static void Field(ref float y, float w, string label, string value, float h)
        {
            PanelKit.Label(new Rect(0f, y, 104f, 26f), label, PanelKit.TextBoldSmall);
            PanelKit.Label(new Rect(110f, y, w - 110f, h), value, PanelKit.TextSmall);
            y += h + 4f;
        }

        void DrawBottom(Rect r)
        {
            // name (the field itself is drawn first in Draw) and a random-name button
            PanelKit.Label(new Rect(60f, r.y, 420f, 34f), "Your name", PanelKit.LHeading);
            if (Ui.Btn(new Rect(488f, 884f, 54f, 50f), "?", Ui.Button, true, "A random name"))
            {
                heroName = RandomName();
                Unfocus();
            }
            // start level
            PanelKit.Label(new Rect(600f, r.y, 440f, 34f), "Start at level", PanelKit.LHeading);
            for (int i = 0; i < Levels.Length; i++)
            {
                var b = new Rect(600f + i * 64f, 884f, 58f, 50f);
                if (Ui.Btn(b, LevelLabels[i], i == levelIndex ? Ui.ButtonGold : Ui.Button, true, i == 0 ? "A fresh start" : "Veteran start: try the talent trees") && i != levelIndex)
                {
                    levelIndex = i;
                    infoScroll.Reset();
                }
            }
            int capKey = levelIndex * 4 + (autoTalents ? 1 : 0) + (veteranGear ? 2 : 0);
            if (capKey != captionKey)
            {
                captionKey = capKey;
                caption = StartLevel == 1
                    ? "A fresh start: the whole story from the first step. Learn your class rank by rank at the trainers."
                    : $"Veteran start: try the talent trees! Every rank up to level {StartLevel} is trained and {Mathf.Max(0, StartLevel - 9)} talent points " +
                      (autoTalents ? "are spent on a recommended build" : "wait for you") + (veteranGear ? ", with level-appropriate gear and gold." : ".");
                levelHeading = StartLevel > 1 ? $"At level {StartLevel}" : "Starting out";
            }
            PanelKit.Label(new Rect(600f, 944f, 760f, 60f), caption, PanelKit.LTextSmall, new Color(1f, 0.94f, 0.85f, 0.9f));
            if (StartLevel > 1)
            {
                veteranGear = LightToggle(new Rect(1066f, 878f, 300f, 26f), "Veteran gear", veteranGear);
                autoTalents = LightToggle(new Rect(1066f, 910f, 300f, 26f), "Spend talent points for me", autoTalents);
            }
            // buttons
            if (Ui.Btn(new Rect(1400f, 886f, 160f, 58f), "Back")) Back();
            if (Ui.Btn(new Rect(1580f, 880f, 280f, 70f), "Begin your journey", Ui.ButtonGold)) Begin();
            if (!string.IsNullOrEmpty(error))
                PanelKit.Label(new Rect(1400f, 960f, 460f, 60f), error, PanelKit.LTextSmall, Ui.Bad);
        }

        static bool LightToggle(Rect r, string label, bool value)
        {
            var box = new Rect(r.x, r.y + 2f, 22f, 22f);
            PanelKit.Rounded(box, PanelKit.Hover(r) ? Ui.Hex("#ffe9b8") : Color.white);
            if (value) PanelKit.Tex(new Rect(box.x + 2f, box.y + 2f, 18f, 18f), PanelArt.Check, PanelKit.GoodDark);
            PanelKit.Label(new Rect(box.xMax + 10f, r.y, r.width - 34f, r.height), label, PanelKit.LTextSmall);
            GameInput.BlockRectGui(r);
            if (PanelKit.LeftClick(r)) { Ui.Sfx?.Invoke("ui_click"); return !value; }
            return value;
        }
    }
}
