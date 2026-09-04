namespace Sim.Core.Domain
{
    /// <summary>
    /// A scheduled league match. The result is stored inline once played;
    /// full MatchReports are not persisted for AI matches (replays are
    /// regenerable from seed + lineups, ARCHITECTURE.md §4.3).
    /// </summary>
    public sealed class Fixture
    {
        public int Id { get; set; }

        /// <summary>1-based matchday (1..2*(N-1) for a double round-robin).</summary>
        public int Round { get; set; }

        /// <summary>Career day this match is played on.</summary>
        public int Day { get; set; }

        public int HomeClubId { get; set; }
        public int AwayClubId { get; set; }

        public bool Played { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }

        public bool Involves(int clubId) => HomeClubId == clubId || AwayClubId == clubId;
    }
}
