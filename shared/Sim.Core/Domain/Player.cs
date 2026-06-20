namespace Sim.Core.Domain
{
    /// <summary>A footballer. Pure data; behaviour lives in the engine systems.</summary>
    public sealed class Player
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int Age { get; set; }
        public PositionRole Role { get; set; }

        public PlayerAttributes Attributes { get; set; } = new PlayerAttributes();
        public PlayerCondition Condition { get; set; } = new PlayerCondition();
        public PlayerDevelopment Development { get; set; } = new PlayerDevelopment();
        public Contract Contract { get; set; } = new Contract();

        /// <summary>
        /// Cached transfer value in game-currency units, refreshed by the valuation
        /// re-pricing pass (task 5.1, see Market.ValuationModel / ValuationProgressor).
        /// 0 until first priced. Stored (not recomputed per read) so the market UI shows a
        /// stable figure that updates on a cadence rather than flickering with daily form.
        /// </summary>
        public long MarketValue { get; set; }

        public string FullName => string.IsNullOrEmpty(FirstName) ? LastName : $"{FirstName} {LastName}";
    }
}
