namespace Sim.Core.Match
{
    public enum MatchEventType
    {
        Goal = 0,
        ChanceSaved = 1,
        ChanceMissed = 2,
        /// <summary>A touchline shout was heard (watchable-match spec R11); <see cref="MatchEvent.Shout"/> says which.</summary>
        Shout = 3
    }

    /// <summary>One entry of the match timeline.</summary>
    public sealed class MatchEvent
    {
        public int Minute { get; set; }
        public MatchEventType Type { get; set; }
        public int ClubId { get; set; }
        /// <summary>The shooter (scorer when Type is Goal).</summary>
        public int PlayerId { get; set; }
        /// <summary>The shout called, when Type is Shout; None otherwise.</summary>
        public TouchlineShout Shout { get; set; }
    }
}
