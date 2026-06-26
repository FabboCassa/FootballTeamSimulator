namespace Fts.Services
{
    /// <summary>
    /// One finished season of the user's coaching career (task 5.6), for the career-history
    /// screen. Plain serialized client data — recorded by <see cref="CareerService"/> at season
    /// end, before the rollover and any club move. Outcome is stored as the int value of
    /// Sim.Core.Career.SeasonOutcome (0 underachieved / 1 met / 2 overachieved) so it stays a
    /// stable save key without coupling the save shape to the enum.
    /// </summary>
    public sealed class CareerHistoryEntry
    {
        public int Year { get; set; }
        public string ClubName { get; set; } = string.Empty;
        public int Division { get; set; } = 1;

        /// <summary>Final league position (1-based).</summary>
        public int FinishPosition { get; set; }

        /// <summary>The board's expected position that season (1-based).</summary>
        public int ExpectedPosition { get; set; }

        /// <summary>int value of Sim.Core.Career.SeasonOutcome.</summary>
        public int Outcome { get; set; }

        /// <summary>Reputation after the season.</summary>
        public int Reputation { get; set; }

        /// <summary>True if the club won its division.</summary>
        public bool Champion { get; set; }

        /// <summary>True if the user was sacked at the end of this season.</summary>
        public bool Sacked { get; set; }
    }
}
