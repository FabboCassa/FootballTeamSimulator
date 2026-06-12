using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Career
{
    /// <summary>
    /// A fixture simulated by SeasonProgressor together with its full report
    /// (events, positions). The report is in-memory only: hosts persist just
    /// the fixture result and can regenerate the rest from the seed.
    /// </summary>
    public sealed class MatchOutcome
    {
        public Fixture Fixture { get; }
        public MatchReport Report { get; }

        public MatchOutcome(Fixture fixture, MatchReport report)
        {
            Fixture = fixture;
            Report = report;
        }
    }
}
