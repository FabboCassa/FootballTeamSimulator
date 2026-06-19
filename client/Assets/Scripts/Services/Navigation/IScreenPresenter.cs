using UnityEngine.UIElements;

namespace Fts.Services.Navigation
{
    /// <summary>
    /// A screen in the navigation stack (MVP presenter). The navigator owns the
    /// lifecycle: the presenter is resolved from a per-screen DI scope on push
    /// and the scope is disposed on pop.
    /// </summary>
    public interface IScreenPresenter
    {
        /// <summary>Root visual element of the screen; the navigator attaches it to the UI root.</summary>
        VisualElement View { get; }

        /// <summary>Called right after the screen is pushed and attached.</summary>
        void Enter();

        /// <summary>Called right before the screen is popped and detached.</summary>
        void Exit();

        /// <summary>Called when the screen becomes top again after a pop (e.g. to refresh stale UI).</summary>
        void Reveal() { }

        /// <summary>
        /// Gives the top screen a chance to intercept the hardware/Esc back
        /// action (e.g. the Hub ends the career instead of being popped raw).
        /// Return true if handled.
        /// </summary>
        bool HandleBack() => false;
    }
}
