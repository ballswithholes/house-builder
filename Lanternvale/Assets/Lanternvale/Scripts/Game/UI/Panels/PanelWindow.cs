// Base class of the toggled panels (UiPanels ids): wraps Draw in the occlusion layer and an exception-safe frame.
using System.Collections.Generic;
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public abstract class PanelWindow : UiPanel
    {
        // every toggled panel, one per type (UiRoot builds its screens once; a replay without a domain reload builds
        // new ones, which replace the old)
        static readonly List<PanelWindow> all = new List<PanelWindow>();

        protected PanelWindow()
        {
            var t = GetType();
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].GetType() == t) all.RemoveAt(i);
            all.Add(this);
        }

        /// <summary>The visible toggled panel drawn on top of the others (highest Order), or null. Esc closes this one
        /// (EscRouter), not the most recently opened panel, which may be hidden or drawn under another window.</summary>
        internal static PanelWindow TopVisible()
        {
            PanelWindow top = null;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                bool visible;
                try { visible = p.Visible; }
                catch (System.Exception) { visible = false; }
                if (visible && (top == null || p.Order > top.Order)) top = p;
            }
            return top;
        }

        /// <summary>Sheets step aside while a conversation runs (they stay open and come back afterwards).</summary>
        protected virtual bool ShowInDialogue => false;

        public override bool Visible => base.Visible && (ShowInDialogue || PanelKit.Mode != Lanternvale.Session.SessionMode.Dialogue);

        public sealed override void Draw()
        {
            PanelKit.EnsureStyles();
            var layer = PanelKit.BeginLayer(Order);
            var color = GUI.color;
            var enabled = GUI.enabled;
            try { DrawPanel(); }
            catch (System.Exception e) when (!(e is ExitGUIException)) { PanelKit.LogOnce(this, e); }
            finally
            {
                GUI.color = color;
                GUI.enabled = enabled;
                PanelKit.EndLayer(layer);
            }
        }

        protected abstract void DrawPanel();

        /// <summary>Standard window chrome; closes the panel when × is clicked.</summary>
        protected Rect Chrome(Rect r, string title, string subtitle = null)
        {
            var c = PanelKit.Window(r, title, Order, out bool close, subtitle);
            if (close) Close();
            return c;
        }
    }
}
