// Title screen (full-screen, Order 300): drawn over the backdrop diorama GameFlow builds (village at dusk, slow camera
// drift). Logo, Continue (latest save), New Game (character creation), Load, Settings, Quit; a soft vignette, a warm
// gradient behind the menu column and lantern motes drifting upwards. Keyboard: Up/Down (W/S) + Enter, Esc goes back.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public sealed class MainMenuScreen : IUiScreen
    {
        enum Page { Home, Load, Settings }

        public string Id => "";
        public int Order => 300;
        public bool Modal => true;

        public bool Visible
        {
            get
            {
                var root = GameRoot.Instance;
                if (root == null || GameFlow.Instance == null || GameFlow.HasGame) return false;
                return root.Mode != GameMode.CharacterCreation;
            }
        }

        static MainMenuScreen instance;
        Page page;
        float shownAt = -1f;
        int focus;
        bool wasVisible;
        SaveSlotInfo latest;
        string continueLabel = "", continueButton = "Continue";
        float latestAt;
        readonly SaveSlotsView loadView = new SaveSlotsView { SaveMode = false };
        readonly SettingsView settings = new SettingsView();

        struct Mote
        {
            public Vector2 Pos;
            public float Speed, Size, Phase, Alpha;
        }

        readonly Mote[] motes = new Mote[34];
        readonly System.Random rng = new System.Random(1234);

        static readonly string[] Labels = { "Continue", "New Game", "Load Game", "Settings", "Quit" };

        public MainMenuScreen()
        {
            instance = this;
            for (int i = 0; i < motes.Length; i++) motes[i] = NewMote(true);
        }

        static MainMenuScreen()
        {
            EscRouter.Register(100, () =>
            {
                var m = instance;
                if (m == null || !m.Visible || m.page == Page.Home) return false;
                m.page = Page.Home;
                Ui.Sfx?.Invoke("ui_close");
                return true;
            });
        }

        Mote NewMote(bool anywhere)
        {
            return new Mote
            {
                Pos = new Vector2((float)rng.NextDouble(), anywhere ? (float)rng.NextDouble() : 1.05f),
                Speed = 0.012f + (float)rng.NextDouble() * 0.02f,
                Size = 6f + (float)rng.NextDouble() * 12f,
                Phase = (float)rng.NextDouble() * 6.28f,
                Alpha = 0.35f + (float)rng.NextDouble() * 0.5f,
            };
        }

        public void Tick(float dt)
        {
            bool vis = Visible;
            if (vis && !wasVisible)
            {
                shownAt = Time.unscaledTime;
                page = Page.Home;
                latestAt = 0f;
                loadView.Invalidate();
            }
            wasVisible = vis;
            if (!vis) return;
            settings.Tick();
            for (int i = 0; i < motes.Length; i++)
            {
                var m = motes[i];
                m.Pos.y -= m.Speed * dt;
                m.Pos.x += Mathf.Sin(Time.unscaledTime * 0.6f + m.Phase) * 0.004f * dt;
                if (m.Pos.y < -0.05f) m = NewMote(false);
                motes[i] = m;
            }
            if (Time.unscaledTime >= latestAt)
            {
                latestAt = Time.unscaledTime + 1.5f;
                try { latest = GameFlow.Instance != null ? GameFlow.Instance.LatestSave() : null; }
                catch (Exception e) { latest = null; Debug.LogException(e); }
                continueLabel = BuildContinueLabel(latest);
                continueButton = continueLabel.Length > 0 ? "Continue\n<size=15>" + continueLabel + "</size>" : "Continue";
            }
            if (page != Page.Home || ConfirmScreen.IsOpen) return;
            int dir = 0;
            if (GameInput.KeyDown(KeyCode.UpArrow) || GameInput.KeyDown(KeyCode.W)) dir = -1;
            if (GameInput.KeyDown(KeyCode.DownArrow) || GameInput.KeyDown(KeyCode.S)) dir = 1;
            if (dir != 0)
            {
                for (int k = 0; k < Labels.Length; k++)
                {
                    focus = (focus + dir + Labels.Length) % Labels.Length;
                    if (Enabled(focus)) break;
                }
                Ui.Sfx?.Invoke("ui_click");
            }
            if (!Enabled(focus)) focus = latest != null ? 0 : 1;
            if (PanelKit.ConfirmKeyDown() || GameInput.KeyDown(KeyCode.Space)) Activate(focus);
        }

        static string BuildContinueLabel(SaveSlotInfo s)
        {
            if (s == null || s.Header == null) return "";
            var h = s.Header;
            var db = PanelKit.Db;
            var cls = db != null ? db.Class(h.playerClass) : null;
            string map = string.IsNullOrEmpty(h.mapName) ? h.mapId : h.mapName;
            return $"{h.playerName} · Level {h.playerLevel} {(cls != null ? cls.name : h.playerClass.ToString())} · {map}";
        }

        bool Enabled(int i) => i != 0 || latest != null;

        void Activate(int i)
        {
            if (!Enabled(i)) return;
            Ui.Sfx?.Invoke("ui_click");
            switch (i)
            {
                case 0:
                    var slot = latest.Slot;
                    PanelKit.Do(() => PanelKit.Try(() => GameFlow.Instance != null ? GameFlow.Instance.LoadFromSlot(slot) : "The game is not ready."));
                    break;
                case 1:
                    PanelKit.Do(() => { if (GameRoot.Instance != null) GameRoot.Instance.SetMode(GameMode.CharacterCreation); });
                    break;
                case 2:
                    page = Page.Load;
                    loadView.Invalidate();
                    break;
                case 3:
                    page = Page.Settings;
                    break;
                case 4:
                    PanelKit.Do(() => { if (GameFlow.Instance != null) GameFlow.Instance.QuitGame(); });
                    break;
            }
        }

        public void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            try
            {
                PanelKit.OccludeAll(Order);
                Ui.Block(new Rect(0f, 0f, Ui.Width, Ui.Height));
                float fade = Mathf.Clamp01((Time.unscaledTime - shownAt) / 1.2f);
                DrawBackdrop(fade);
                if (page == Page.Home) DrawHome(fade);
                else if (page == Page.Load) DrawLoad();
                else DrawSettings();
            }
            catch (Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally { PanelKit.EndLayer(layer); }
        }

        void DrawBackdrop(float fade)
        {
            if (!PanelKit.IsRepaint) return;
            float w = Ui.Width, h = Ui.Height;
            var vignette = ArtLibrary.HasRealTexture("ui_vignette") ? ArtLibrary.Texture("ui_vignette") : null;
            if (vignette != null) PanelKit.Tex(new Rect(-w * 0.08f, -h * 0.08f, w * 1.16f, h * 1.16f), vignette, new Color(1f, 1f, 1f, 0.9f * fade));
            // warm-dusk gradient behind the menu column and a soft floor shade
            PanelKit.Tex(new Rect(0f, 0f, Mathf.Min(w * 0.55f, 900f), h), PanelArt.GradientRight, new Color(0.10f, 0.07f, 0.16f, 0.62f * fade));
            PanelKit.Tex(new Rect(0f, h * 0.7f, w, h * 0.3f), PanelArt.GradientUp, new Color(0.08f, 0.06f, 0.12f, 0.45f * fade));
            // lantern motes
            for (int i = 0; i < motes.Length; i++)
            {
                var m = motes[i];
                float tw = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 2.1f + m.Phase * 3f);
                var p = new Vector2(m.Pos.x * w, m.Pos.y * h);
                float s = m.Size;
                PanelKit.Tex(new Rect(p.x - s * 1.6f, p.y - s * 1.6f, s * 3.2f, s * 3.2f), ProceduralArt.Glow, new Color(1f, 0.78f, 0.42f, 0.35f * m.Alpha * tw * fade));
                PanelKit.Tex(new Rect(p.x - s * 0.25f, p.y - s * 0.25f, s * 0.5f, s * 0.5f), PanelArt.Disc, new Color(1f, 0.93f, 0.7f, m.Alpha * tw * fade));
            }
        }

        void DrawLogo(Rect r, float fade)
        {
            if (ArtLibrary.HasRealTexture("logo_lanternvale"))
            {
                float bob = Mathf.Sin(Time.unscaledTime * 0.8f) * 4f;
                PanelKit.Tex(new Rect(r.x, r.y + bob, r.width, r.height), ArtLibrary.Texture("logo_lanternvale"), new Color(1f, 1f, 1f, fade), ScaleMode.ScaleToFit);
            }
            else
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, fade);
                Ui.Shadowed(r, "Lanternvale", PanelKit.LTitle);
                GUI.color = old;
            }
        }

        void DrawHome(float fade)
        {
            float h = Ui.Height;
            float colX = Mathf.Max(60f, Ui.Width * 0.07f);
            const float bw = 400f, gap = 14f, contH = 74f, btnH = 58f;
            const float hintY = 46f;      // the footer hint sits at h - hintY
            const float errH = 64f;       // error line below the column (y + 4, 60 high)
            var err = GameFlow.Instance != null ? GameFlow.Instance.LastError : "";
            bool hasErr = !string.IsNullOrEmpty(err);
            // The column must end above the footer hint (with room for the focus glow) on every canvas height: at the
            // 150% interface size the canvas is only 720 high, so the logo gives up height first, then the top margin.
            float column = (latest != null ? contH : btnH) + (Labels.Length - 1) * btnH + (Labels.Length - 1) * gap;
            float bottom = h - hintY - 14f - (hasErr ? errH : 0f);
            float top = h * 0.06f;
            float logoH = Mathf.Clamp(bottom - column - 24f - top, 120f, 320f);
            top = Mathf.Clamp(bottom - column - 24f - logoH, 8f, top);
            float k = logoH / 320f;
            var logo = new Rect(colX - 40f * k, top, 640f * k, logoH);
            DrawLogo(logo, fade);
            PanelKit.Label(new Rect(colX + 10f, logo.yMax - 34f * k, 560f, 30f), "<i>The spirit-lanterns are going dark…</i>", PanelKit.LTextSmall, new Color(1f, 0.93f, 0.8f, 0.85f * fade));

            float y = logo.yMax + 24f;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            for (int i = 0; i < Labels.Length; i++)
            {
                bool cont = i == 0;
                float bh = cont && latest != null ? contH : btnH;
                var r = new Rect(colX, y, bw, bh);
                bool en = Enabled(i);
                if (en && PanelKit.Hover(r) && Event.current.type == EventType.Repaint) focus = i;
                if (focus == i && en)
                    PanelKit.Tex(new Rect(r.x - 18f, r.y - 14f, r.width + 36f, r.height + 28f), ProceduralArt.Glow, new Color(1f, 0.8f, 0.45f, 0.55f));
                string label = Labels[i];
                if (cont && latest != null) label = continueButton;
                if (Ui.Btn(r, label, i == 0 || (i == 1 && latest == null) ? Ui.ButtonGold : Ui.Button, en, cont && !en ? "No saved game yet." : null)) Activate(i);
                y += bh + gap;
            }
            GUI.color = old;
            // laid out from the last button (y is its bottom + gap)
            float end = y - gap + 14f;
            if (hasErr)
            {
                PanelKit.Label(new Rect(colX, y + 4f, 520f, 60f), err, PanelKit.LTextSmall, Ui.Bad);
                end = y + 4f + 60f;
            }
            // the hint only where it does not run into the column (a canvas under 720 high: a window below 540 px)
            if (end <= h - hintY + 1f)
                PanelKit.Label(new Rect(colX, h - hintY, 700f, 30f), "Arrow keys choose · Enter confirms · a cosy tale of lanterns and lost spirits", PanelKit.LMuted, new Color(1f, 1f, 1f, 0.55f * fade));
        }

        void DrawLoad()
        {
            var r = PanelKit.Centered(980f, 760f);
            var c = PanelKit.Window(r, "Load Game", Order, out bool close);
            if (close) page = Page.Home;
            loadView.SaveMode = false;
            loadView.Draw(new Rect(c.x, c.y, c.width, c.height - 64f));
            if (Ui.Btn(new Rect(c.x, c.yMax - 50f, 160f, 48f), "Back")) page = Page.Home;
        }

        void DrawSettings()
        {
            float h = settings.Height + 170f;
            var r = PanelKit.Centered(820f, h);
            var c = PanelKit.Window(r, "Settings", Order, out bool close);
            if (close) page = Page.Home;
            settings.Draw(new Rect(c.x + 10f, c.y, c.width - 20f, c.height - 64f));
            if (Ui.Btn(new Rect(c.x, c.yMax - 50f, 160f, 48f), "Back")) page = Page.Home;
        }
    }
}
