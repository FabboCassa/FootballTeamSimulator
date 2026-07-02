namespace Fts.Services.Navigation
{
    /// <summary>Hub navigation/calendar actions reachable by a desktop keyboard shortcut (Roadmap 6.5).</summary>
    public enum HubShortcut
    {
        AdvanceDay,
        NextMatch,
        Inbox,
        Squad,
        Tactics,
        Training,
        Support,
        Market,
        Scouting,
        Club,
        Career,
        League,
        // Task 6.6: also raised by the persistent shell (sidebar / Continue button), which
        // pops back to the Hub first so HubPresenter can safely act on them from anywhere.
        EndSeason,
        ExitCareer
    }

    /// <summary>
    /// Published by the desktop keyboard layer (DesktopController) when a Hub hotkey is pressed.
    /// Only the HubPresenter acts on it, and the controller only publishes while the Hub is the
    /// top screen and no modal overlay is open, so the shortcut can never push onto another screen.
    /// </summary>
    public readonly struct HubShortcutMessage
    {
        public readonly HubShortcut Action;

        public HubShortcutMessage(HubShortcut action)
        {
            Action = action;
        }
    }
}
