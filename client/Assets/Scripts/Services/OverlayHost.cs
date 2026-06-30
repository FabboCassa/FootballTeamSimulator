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
        private int _modalCount;

        /// <summary>Bind the overlay layer to the UI root (called once at boot, like the navigator).</summary>
        public void SetRoot(VisualElement root) => _root = root;

        /// <summary>
        /// True while a modal overlay (a confirm dialog or the onboarding step carousel — anything
        /// shown via <see cref="Show"/>) is on screen. Transient toasts (<see cref="ShowTimed"/>) do
        /// NOT count. The desktop keyboard layer (task 6.5) uses this to suppress Esc-back and Hub
        /// hotkeys while a modal is up, so keys go to the dialog instead of the screen behind it.
        /// </summary>
        public bool IsModalOpen => _modalCount > 0;

        /// <summary>
        /// Shows an overlay on top of everything and returns a dismiss action that removes it.
        /// A no-op (returns an empty action) if the root isn't bound yet.
        /// </summary>
        public Action Show(VisualElement overlay)
        {
            if (_root == null || overlay == null)
                return () => { };

            _modalCount++;
            // Decrement however it's removed — via the returned dismiss action OR a self-removal
            // (e.g. the onboarding overlay removes itself when finished) — so the count never sticks.
            overlay.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_modalCount > 0)
                    _modalCount--;
            });

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
