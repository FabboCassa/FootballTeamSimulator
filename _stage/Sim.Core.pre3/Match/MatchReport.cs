using System.Collections.Generic;

namespace Sim.Core.Match
{
    /// <summary>
    /// Complete, serializable outcome of a simulated match.
    /// Online: clients re-render a match from (seed + lineups) and can verify
    /// the server's report by re-running the engine - keep this minimal and stable.
    /// </summary>
    public sealed class MatchReport
    {
        /// <summary>Bump when engine changes invalidate old replays.</summary>
        public int EngineVersion { get; set; } = MatchEngine.Version;

        public int HomeClubId { get; set; }
        public int AwayClubId { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }

        public List<MatchEvent> Events { get; set; } = new List<MatchEvent>();

        /// <summary>
        /// Top-down replay positions (task 1.5). Derived deterministically from
        /// (seed + lineups): regenerable, so it should be stripped before
        /// persisting or sending the report over the network.
        /// </summary>
        public PositionStream? Positions { get; set; }
    }
}
