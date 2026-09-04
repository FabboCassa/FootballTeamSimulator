using System.Collections.Generic;

namespace Sim.Core.Domain
{
    /// <summary>
    /// One league season: the fixture list and the calendar position.
    /// Rollover (promotion/relegation, aging) arrives in task 2.7.
    /// </summary>
    public sealed class Season
    {
        public int Year { get; set; } = 1;

        /// <summary>Career day, starting at 1. Advanced by the host via SeasonProgressor.</summary>
        public int CurrentDay { get; set; } = 1;

        public List<Fixture> Fixtures { get; set; } = new List<Fixture>();

        /// <summary>
        /// Season scorer counts, in first-goal order (stable and deterministic).
        /// Additive since save v2: older saves start counting from upgrade.
        /// </summary>
        public List<ScorerTally> Scorers { get; set; } = new List<ScorerTally>();
    }
}
