namespace Sim.Core.Generation
{
    /// <summary>
    /// Identity parameters of the league to generate (who/what, not balance).
    /// All gameplay tunables live in BalanceConfig.Generation.
    /// </summary>
    public sealed class LeagueGenerationOptions
    {
        public int LeagueId { get; set; } = 1;
        public string LeagueName { get; set; } = "Lega Cartone";
        public int Division { get; set; } = 1;
        public int ClubCount { get; set; } = 20;

        /// <summary>First id assigned to generated players (clubs use 1..ClubCount).</summary>
        public int FirstPlayerId { get; set; } = 1;
    }
}
