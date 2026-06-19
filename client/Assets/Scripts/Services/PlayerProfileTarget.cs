namespace Fts.Services
{
    /// <summary>
    /// Hands the selected player id to the pushed Player profile screen (task 4.6).
    /// The profile screen takes no constructor argument (ScreenNavigator.Push resolves
    /// presenters from DI with no parameters), so the opener sets <see cref="PlayerId"/>
    /// here just before pushing — the same handoff pattern as UserMatchContextHolder.
    /// Lives in the Game scope; not persisted (purely transient navigation state).
    /// </summary>
    public sealed class PlayerProfileTarget
    {
        /// <summary>Id of the player whose profile should be shown.</summary>
        public int PlayerId { get; set; }
    }
}
