namespace Sim.Core.Domain
{
    /// <summary>
    /// How much of a league is actually simulated (task 11.1). The world can hold far more
    /// leagues than a phone can run week by week, so every league carries the level of
    /// detail it was loaded at. The split mirrors Football Manager's playable/background
    /// nations, and it is what keeps a 100k-player world from running 2,000 full matches
    /// every matchday.
    /// </summary>
    public enum LeagueDetailLevel
    {
        /// <summary>
        /// Full detail: fixtures in the career season, the real <see cref="Match.MatchEngine"/>,
        /// tables, transfers, promotion/relegation. This is what every league was before 11.1.
        /// </summary>
        Playable = 0,

        /// <summary>
        /// Simulated cheaply: its own fixture list lives in <see cref="World.BackgroundSeason"/>
        /// and results come from <see cref="Career.QuickResultResolver"/> (no minute-by-minute
        /// engine, no position streams). Tables and promotion/relegation still work, squads are
        /// smaller, and the clubs are real scouting targets.
        /// </summary>
        Background = 1,

        /// <summary>
        /// Not simulated at all: the clubs exist with a handful of key players so scouts have
        /// something to find there, but there are no fixtures, no table and no season.
        /// </summary>
        DataOnly = 2
    }
}
