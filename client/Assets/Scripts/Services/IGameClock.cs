namespace Fts.Services
{
    /// <summary>
    /// Game calendar (ARCHITECTURE.md §5.2). Single player advances on demand
    /// (LocalClock); online mode will bind a server-driven implementation.
    /// </summary>
    public interface IGameClock
    {
        int CurrentDay { get; }

        void AdvanceDay();
    }

    /// <summary>Published after every day advance.</summary>
    public readonly struct DayAdvancedMessage
    {
        public readonly int Day;
        public readonly int MatchesPlayed;

        /// <summary>True when the user club's fixture was among the simulated matches (outcome in UserMatchLog).</summary>
        public readonly bool UserMatchPlayed;

        public DayAdvancedMessage(int day, int matchesPlayed, bool userMatchPlayed)
        {
            Day = day;
            MatchesPlayed = matchesPlayed;
            UserMatchPlayed = userMatchPlayed;
        }
    }
}
