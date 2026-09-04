using Sim.Core.Domain;

namespace Sim.Core.Generation
{
    /// <summary>
    /// What a database-size choice actually means (task 11.1). The playable nations are always
    /// loaded whole; this decides how much of the REST of the world exists around them, which is
    /// the knob that drives generation time, memory and save size.
    ///
    /// Every field is a plain number so a Custom preset is just "set them yourself".
    /// </summary>
    public sealed class DatabaseSizePreset
    {
        /// <summary>Nations below this reputation are not loaded at all.</summary>
        public int MinNationReputation { get; set; }

        /// <summary>
        /// Non-playable nations at or above this reputation run at
        /// <see cref="LeagueDetailLevel.Background"/> (cheap results, real tables, real
        /// promotion/relegation); the rest are <see cref="LeagueDetailLevel.DataOnly"/>.
        /// Set above 100 to have no background leagues at all.
        /// </summary>
        public int BackgroundMinReputation { get; set; }

        /// <summary>How many tiers of a background nation are simulated (capped by what it has).</summary>
        public int BackgroundTiers { get; set; }

        /// <summary>How many tiers of a data-only nation are loaded as clubs (capped by what it has).</summary>
        public int DataOnlyTiers { get; set; }

        /// <summary>Squad size of a background club. 22 = the same squad a playable club gets.</summary>
        public int BackgroundSquadSize { get; set; }

        /// <summary>How many "key players" a data-only club holds — enough for a scout to find someone.</summary>
        public int DataOnlyPlayersPerClub { get; set; }

        /// <summary>
        /// The three shipped presets. The numbers are a starting point measured by the harness bench
        /// (tools/BalanceHarness, scenario "world"); the DEFAULT for the shipped game is whatever the
        /// weakest target (WebGL, mid-range Android) survives, which is a measurement, not a guess.
        /// </summary>
        public static DatabaseSizePreset For(DatabaseSize size)
        {
            switch (size)
            {
                case DatabaseSize.Small:
                    return new DatabaseSizePreset
                    {
                        MinNationReputation = 70,
                        BackgroundMinReputation = 101, // nothing outside the playable nations is simulated
                        BackgroundTiers = 0,
                        DataOnlyTiers = 1,
                        BackgroundSquadSize = 18,
                        DataOnlyPlayersPerClub = 5
                    };

                case DatabaseSize.Large:
                    return new DatabaseSizePreset
                    {
                        MinNationReputation = 0,
                        BackgroundMinReputation = 60,
                        BackgroundTiers = 2,
                        DataOnlyTiers = 2,
                        BackgroundSquadSize = 20,
                        DataOnlyPlayersPerClub = 11
                    };

                case DatabaseSize.Custom:
                case DatabaseSize.Medium:
                default:
                    return new DatabaseSizePreset
                    {
                        MinNationReputation = 56,
                        BackgroundMinReputation = 78,
                        BackgroundTiers = 1,
                        DataOnlyTiers = 1,
                        BackgroundSquadSize = 18,
                        DataOnlyPlayersPerClub = 7
                    };
            }
        }
    }
}
