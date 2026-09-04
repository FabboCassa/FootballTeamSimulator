namespace Sim.Core.Generation
{
    /// <summary>
    /// Deterministic id allocation for a multi-nation world (task 11.1).
    ///
    /// The rule that matters: an id depends ONLY on (nation index, tier, slot) — never on which
    /// other nations happen to be loaded. So a club keeps its id whether the player picked a Small
    /// or a Large database, and a save can be reasoned about across scopes.
    ///
    /// The ranges start well above the ids the pre-11.1 two-division world used (clubs 1..120,
    /// players 1..5500), so a legacy world and a generated one can never collide.
    /// </summary>
    public static class WorldIds
    {
        // --- Leagues ---
        public const int FirstLeagueId = 1000;
        public const int LeaguesPerNation = 100;

        // --- Clubs ---
        public const int FirstClubId = 1000000;
        public const int ClubsPerNation = 2000;
        public const int ClubsPerTier = 200;

        // --- Players ---
        public const int FirstPlayerId = 10000000;
        public const int PlayersPerNation = 100000;
        public const int PlayersPerTier = 10000;

        /// <summary>
        /// First id handed out to a player created after generation (squad top-ups when a club is
        /// promoted into a fuller-detail tier). Far above every generated block.
        /// </summary>
        public const int FirstDynamicPlayerId = 900000000;

        /// <summary>Tiers a nation may hold before its id block overflows into the next nation's.</summary>
        public const int MaxTiersPerNation = PlayersPerNation / PlayersPerTier;

        /// <summary>Clubs a single tier may hold.</summary>
        public const int MaxClubsPerTier = ClubsPerTier;

        /// <summary>Players a single tier may hold (clubs * squad size).</summary>
        public const int MaxPlayersPerTier = PlayersPerTier;

        public static int LeagueId(int nationIndex, int tier) =>
            FirstLeagueId + nationIndex * LeaguesPerNation + tier;

        public static int FirstClubOf(int nationIndex, int tier) =>
            FirstClubId + nationIndex * ClubsPerNation + (tier - 1) * ClubsPerTier;

        public static int FirstPlayerOf(int nationIndex, int tier) =>
            FirstPlayerId + nationIndex * PlayersPerNation + (tier - 1) * PlayersPerTier;
    }
}
