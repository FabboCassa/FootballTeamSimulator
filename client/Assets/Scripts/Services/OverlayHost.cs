using System;
using UnityEngine.UIElements;

namespace Fts.Services
{
    /// <summary>
    /// Owns the on-top overlay layer of the UI (task 6.2): modal confirm dialogs and transient
    /// toasts are added to the same root the ScreenNavigator draws into, kept above the current
    /// screen. App-scope singleton; its root is set once at startup (AppEntryPoint). Knows nothing
    /// about how an overlay looks — it just shows/hides a <see cref="VisualElement"/> built by the
    /// Views layer, so the layering (Services ⟶ no Views dependency) stays intact.
    /// </summary>
    public sealed class OverlayHost
    {
        private VisualElement _root;

        /// <summary>Bind the overlay layer to the UI root (called once at boot, like the navigator).</summary>
        public void SetRoot(VisualElement root) => _root = root;

        /// <summary>
        /// Shows an overlay on top of everything and returns a dismiss action that removes it.
        /// A no-op (returns an empty action) if the root isn't bound yet.
        /// </summary>
        public Action Show(VisualElement overlay)
        {
            if (_root == null || overlay == null)
                return () => { };

            _root.Add(overlay);
            overlay.BringToFront();
            return () =>
            {
                if (overlay.parent != null)
                    overlay.RemoveFromHierarchy();
            };
        }

        /// <summary>Shows an overlay (typically a toast) and auto-removes it after <paramref name="durationMs"/>.</summary>
        public void ShowTimed(VisualElement overlay, int durationMs)
        {
            if (_root == null || overlay == null)
                return;

            _root.Add(overlay);
            overlay.BringToFront();
            overlay.schedule.Execute(() =>
            {
                if (overlay.parent != null)
                    overlay.RemoveFromHierarchy();
            }).StartingIn(durationMs);
        }
    }
}
