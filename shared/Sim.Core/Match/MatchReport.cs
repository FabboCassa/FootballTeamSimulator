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

        /// <summary>
        /// What every man did, and what the two shapes looked like (engine phase 7). Present only
        /// for a match that was PLAYED — the fast path of <see cref="MatchEngine"/> has no picture
        /// to count, and a match with no picture has no statistics either.
        ///
        /// Derived: <see cref="Analysis.MatchStatsBuilder"/> reads the finished report and its
        /// stream and touches neither, so it consumes no randomness and moves no result. It is
        /// deliberately outside <see cref="MatchReportHasher"/> for the same reason — a golden
        /// master from before this phase is still the right number after it.
        ///
        /// A few kilobytes against the stream's several hundred, so it survives the strip that
        /// removes the positions before a report is persisted. A host that does not want it can
        /// null it out just as freely.
        /// </summary>
        public Analysis.MatchStats? Stats { get; set; }
    }
}
