// Base class of the toggled panels (UiPanels ids): wraps Draw in the occlusion layer and an exception-safe frame.
using UnityEngine;

namespace Lanternvale.Game.Panels
{
    public abstract class PanelWindow : UiPanel
    {
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
