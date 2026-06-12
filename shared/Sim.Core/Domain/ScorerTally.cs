namespace Sim.Core.Domain
{
    /// <summary>
    /// Season goal count of one player. Accumulated by SeasonProgressor as
    /// matches are simulated (full reports are not persisted, tallies are).
    /// </summary>
    public sealed class ScorerTally
    {
        public int PlayerId { get; set; }

        /// <summary>Club the player scored for (at the time of scoring).</summary>
        public int ClubId { get; set; }

        public int Goals { get; set; }
    }
}
