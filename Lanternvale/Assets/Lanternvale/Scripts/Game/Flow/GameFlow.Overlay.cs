// GameFlow overlay and small cinematics:
//   * fades (map arrival, title backdrop, long rest: fade out → the clock jumps to morning → fade in),
//   * a warm flash for story beats,
//   * NPC barks (speech bubbles above NPCs on hover / when they have nothing else to say),
//   * the lantern rekindling sequence (every dark spirit lantern relights one after another).
// Drawn with plain GUI (no layout) at GUI.depth 8: above floating combat text, below the regular UI.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public sealed partial class GameFlow
    {
        const int OverlayGuiDepth = 8;

        // ------------------------------------------------------------ fades

        enum FadePhase { None, In, RestOut, RestHold, RestIn }

        FadePhase fadePhase;
        float fadeAlpha, fadeSpeed, fadeHold;
        float? restHoldHour;
        static readonly Color FadeColor = new Color(0.07f, 0.06f, 0.12f, 1f);

        float flashAlpha;
        Color flashColor = new Color(1f, 0.86f, 0.5f, 1f);

        /// <summary>True while a full-screen fade hides the world (clicks would be blind).</summary>
        bool FadeBlocksInput => fadeAlpha > 0.6f && fadePhase != FadePhase.None;

        void BeginFadeIn(float seconds)
        {
            if (fadePhase == FadePhase.RestOut || fadePhase == FadePhase.RestHold) return;
            fadeAlpha = Mathf.Max(fadeAlpha, 1f);
            fadeSpeed = 1f / Mathf.Max(0.05f, seconds);
            fadePhase = FadePhase.In;
        }

        void BeginRestFade()
        {
            // keep showing the evening until the screen is dark, then let the clock jump to the morning
            restHoldHour = DayNight.WorldHour;
            fadePhase = FadePhase.RestOut;
            fadeSpeed = 1f / 0.8f;
            fadeHold = 0.7f;
        }

        void Flash(Color c, float alpha)
        {
            flashColor = c;
            flashAlpha = Mathf.Max(flashAlpha, alpha);
        }

        /// <summary>Hour shown by the lighting (the session's clock, held during the rest fade-out).</summary>
        float DisplayHour()
        {
            if (restHoldHour.HasValue) return restHoldHour.Value;
            return Session != null ? Session.GameHour : DayNight.WorldHour;
        }

        void UpdateOverlay(float dt)
        {
            dt = Mathf.Min(dt, 0.1f);
            switch (fadePhase)
            {
                case FadePhase.In:
                    fadeAlpha -= fadeSpeed * dt;
                    if (fadeAlpha <= 0f) { fadeAlpha = 0f; fadePhase = FadePhase.None; }
                    break;
                case FadePhase.RestOut:
                    fadeAlpha += fadeSpeed * dt;
                    if (fadeAlpha >= 1f) { fadeAlpha = 1f; fadePhase = FadePhase.RestHold; }
                    break;
                case FadePhase.RestHold:
                    restHoldHour = null;
                    fadeHold -= dt;
                    if (fadeHold <= 0f)
                    {
                        fadePhase = FadePhase.RestIn;
                        fadeSpeed = 1f / 1.4f;
                        try { OnRestWake(); } catch (Exception e) { Debug.LogException(e); }
                    }
                    break;
                case FadePhase.RestIn:
                    fadeAlpha -= fadeSpeed * dt;
                    if (fadeAlpha <= 0f) { fadeAlpha = 0f; fadePhase = FadePhase.None; }
                    break;
            }
            if (flashAlpha > 0f) flashAlpha = Mathf.Max(0f, flashAlpha - dt * 0.6f);

            for (int i = barks.Count - 1; i >= 0; i--)
            {
                var b = barks[i];
                b.Age += dt;
                if (b.Age >= b.Life || b.View == null) barks.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------ barks

        sealed class Bark
        {
            public UnitView View;
            public GUIContent Content;
            public float Age, Life, Height;
        }

        readonly List<Bark> barks = new List<Bark>();
        GUIStyle barkStyle;
        const float BarkWidth = 300f;

        void ShowBark(UnitView v, string text)
        {
            if (v == null || string.IsNullOrEmpty(text)) return;
            for (int i = barks.Count - 1; i >= 0; i--) if (ReferenceEquals(barks[i].View, v)) barks.RemoveAt(i);
            if (barks.Count >= 3) barks.RemoveAt(0);
            barks.Add(new Bark
            {
                View = v,
                Content = new GUIContent(text),
                Life = Mathf.Clamp(2.6f + text.Length * 0.045f, 3f, 7.5f),
                Height = -1f,
            });
        }

        void ForgetBarksOf(UnitView v)
        {
            for (int i = barks.Count - 1; i >= 0; i--) if (ReferenceEquals(barks[i].View, v)) barks.RemoveAt(i);
        }

        void ClearBarks() => barks.Clear();

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            var ev = Event.current;
            if (ev == null || ev.type != EventType.Repaint) return;
            if (barks.Count == 0 && fadeAlpha <= 0.001f && flashAlpha <= 0.001f) return;
            try
            {
                GUI.depth = OverlayGuiDepth;
                var oldColor = GUI.color;
                var oldMatrix = GUI.matrix;
                if (barks.Count > 0) DrawBarks();
                GUI.matrix = Matrix4x4.identity;
                var full = new Rect(0f, 0f, Screen.width, Screen.height);
                if (flashAlpha > 0.001f)
                {
                    GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashAlpha);
                    GUI.DrawTexture(full, Texture2D.whiteTexture);
                }
                if (fadeAlpha > 0.001f)
                {
                    float a = fadeAlpha * fadeAlpha * (3f - 2f * fadeAlpha);   // smoothstep
                    GUI.color = new Color(FadeColor.r, FadeColor.g, FadeColor.b, a);
                    GUI.DrawTexture(full, Texture2D.whiteTexture);
                }
                GUI.color = oldColor;
                GUI.matrix = oldMatrix;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void DrawBarks()
        {
            var rig = CameraRig.Instance;
            if (rig == null || Ui.LabelSmall == null || Ui.InkPanelSoft == null) return;
            if (barkStyle == null)
            {
                barkStyle = new GUIStyle(Ui.LabelSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Italic };
                barkStyle.normal.textColor = Ui.TextLight;
            }
            float scale = Mathf.Max(0.5f, Screen.height / Ui.RefHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            for (int i = 0; i < barks.Count; i++)
            {
                var b = barks[i];
                if (b.View == null || !b.View.Visible) continue;
                if (b.Height < 0f) b.Height = barkStyle.CalcHeight(b.Content, BarkWidth - 28f) + 18f;
                float a = Mathf.Clamp01(b.Age / 0.2f) * Mathf.Clamp01((b.Life - b.Age) / 0.45f);
                if (a <= 0.01f) continue;
                var gui = rig.WorldToGui(b.View.NameplatePosition + new Vector2(0f, 0.3f)) / scale;
                float rise = (1f - Mathf.Clamp01(b.Age / 0.25f)) * 8f;
                var r = new Rect(gui.x - BarkWidth * 0.5f, gui.y - b.Height - 6f + rise, BarkWidth, b.Height);
                // keep it on screen
                float w = Screen.width / scale, h = Screen.height / scale;
                r.x = Mathf.Clamp(r.x, 8f, Mathf.Max(8f, w - r.width - 8f));
                r.y = Mathf.Clamp(r.y, 8f, Mathf.Max(8f, h - r.height - 8f));
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Box(r, GUIContent.none, Ui.InkPanelSoft);
                GUI.Label(new Rect(r.x + 14f, r.y + 9f, r.width - 28f, r.height - 18f), b.Content, barkStyle);
            }
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------ lanterns

        readonly List<MapObject> lanternQueue = new List<MapObject>();
        int lanternIndex;
        float lanternTimer;
        bool lanternSequence;

        void OnRekindleLanterns(bool animate)
        {
            var map = MapView.Current;
            if (map == null) return;
            if (!animate)
            {
                map.SetAllLanternsLit(true, false);
                return;
            }
            ClearLanternSequence();
            foreach (var o in map.Objects) if (o.IsLantern && !o.LanternLit) lanternQueue.Add(o);
            var from = LeaderFeet();
            lanternQueue.Sort((a, b) => (a.Position - from).sqrMagnitude.CompareTo((b.Position - from).sqrMagnitude));
            lanternSequence = true;
            lanternTimer = 0.6f;
            Flash(new Color(1f, 0.85f, 0.5f, 1f), 0.42f);
            Sfx.Play("quest");
            if (CameraRig.Instance != null) CameraRig.Instance.Shake(0.05f, 0.35f);
            if (Session != null)
                foreach (var u in Session.PartyUnits())
                {
                    var v = ViewOf(u);
                    if (v != null) FxSystem.Sparkles(v.CenterPosition, new Color(1f, 0.88f, 0.55f), 14);
                }
        }

        void UpdateLanternSequence(float dt)
        {
            if (!lanternSequence) return;
            var map = MapView.Current;
            if (map == null) { ClearLanternSequence(); return; }
            lanternTimer -= dt;
            if (lanternTimer > 0f) return;
            if (lanternIndex < lanternQueue.Count)
            {
                var o = lanternQueue[lanternIndex++];
                if (!o.LanternLit && !string.IsNullOrEmpty(o.Id) && map.Find(o.Id) == o)
                {
                    map.SetLanternLit(o.Id, true, true);
                    Sfx.Play("impact_holy", o.Position, 0.75f, 0.95f + 0.04f * Mathf.Min(lanternIndex, 8));
                }
                lanternTimer = 0.38f;
                return;
            }
            // finale: anything left (lanterns without an id) and a last chime
            map.SetAllLanternsLit(true, true);
            Sfx.Play("level_up", null, 0.8f, 1f);
            if (Session != null)
                foreach (var u in Session.PartyUnits())
                {
                    var v = ViewOf(u);
                    if (v != null) FxSystem.AuraPulse(v.FeetPosition, 1.6f, new Color(1f, 0.86f, 0.55f, 0.85f));
                }
            ClearLanternSequence();
        }

        void ClearLanternSequence()
        {
            lanternQueue.Clear();
            lanternIndex = 0;
            lanternTimer = 0f;
            lanternSequence = false;
        }
    }
}
