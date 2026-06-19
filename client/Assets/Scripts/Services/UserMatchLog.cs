using Sim.Core.Career;

namespace Fts.Services
{
    /// <summary>
    /// In-memory handoff of the user's latest match outcome (set by LocalClock,
    /// read by the match result screen). Not persisted: reports are regenerable
    /// from the seed, and only the fixture result lives in the save.
    /// Lives in the Game scope.
    /// </summary>
    public sealed class UserMatchLog
    {
        public MatchOutcome LastMatch { get; set; }
    }
}
