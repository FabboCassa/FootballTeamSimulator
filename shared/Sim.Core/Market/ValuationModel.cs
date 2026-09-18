using Sim.Core.Config;
using Sim.Core.Development;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// The player valuation model (task 5.1, ARCHITECTURE.md §4.7). Turns a player's state
    /// into a transfer price. PURE and deterministic: integer/long math only, no
    /// transcendental functions and NO RNG — a player's value is a stable function of his
    /// state, so the same player always prices identically on .NET / Mono / IL2CPP, and the
    /// host can re-price on whatever cadence it likes without perturbing any seeded stream.
    ///
    /// The price is built from (all magnitudes in <see cref="MarketBalance"/>; the structural
    /// shape lives here, like the +/- patterns in TacticModifiers / the role offsets in AgeCurve):
    ///
    ///   1. a <b>value rating</b> = overall + a youth-scaled share of the remaining potential
    ///      headroom. The youth scaling reuses <see cref="AgeCurve.GrowthFactorPermille"/> (1000
    ///      for a teenager, 0 at the role's peak), so a 19-year-old with high potential is priced
    ///      well above his current overall while a 30-year-old at the same overall gets no premium
    ///      — "young stars cost more than equal-ability 30-year-olds";
    ///   2. a convex <b>base value</b> = ValueUnit × (valueRating − RatingValueFloor)^Exponent,
    ///      so ability and value correlate and top talents cost disproportionately more;
    ///   3. permille <b>multipliers</b> for age (resale value fades after the prime), short-term
    ///      form (small), contract length (an expiring deal discounts the fee) and league level;
    ///   4. a clamp to [MinValue, MaxValue] and rounding — so a price is always positive and
    ///      never absurd (the 5.1 acceptance).
    ///
    /// Scouting uncertainty is NOT modelled here: this prices the TRUE state. The market/UI
    /// layer (task 5.4) applies scouting accuracy on top — uncertainty lives in knowledge,
    /// not in the sim (ARCHITECTURE.md §4.7).
    /// </summary>
    public static class ValuationModel
    {
        /// <summary>
        /// Nation economic reputation used by the overloads that don't take one: a top-tier nation
        /// (no discount) — the pre-R8 baseline, so every pre-existing caller keeps pricing exactly
        /// as before until it is wired to a real <see cref="League.EconomicReputation"/>.
        /// </summary>
        private const int DefaultEconomicReputation = 100;

        /// <summary>Transfer value of a player in the top division (level 1) of a top-tier nation.</summary>
        public static long Value(Player player, BalanceConfig cfg) =>
            Value(player, leagueLevel: 1, DefaultEconomicReputation, cfg);

        /// <summary>
        /// Transfer value of a player in game-currency units. <paramref name="leagueLevel"/>
        /// is the division he plays in (1 = top flight); lower divisions discount the fee.
        /// </summary>
        public static long Value(Player player, int leagueLevel, BalanceConfig cfg) =>
            Value(player, leagueLevel, DefaultEconomicReputation, cfg);

        /// <summary>
        /// Transfer value of a player in game-currency units (task: player market value by league,
        /// R8). <paramref name="leagueLevel"/> is the division he plays in (1 = top flight);
        /// <paramref name="economicReputation"/> is that division's nation (1..100, see
        /// <see cref="League.EconomicReputation"/>). Both discount the fee through the SAME
        /// nation × division curve club income uses (<see cref="FinanceModel.NationDivisionMultiplierPermille"/>)
        /// instead of a duplicated division-only one — so a player is worth more in a richer
        /// nation's top flight than a poorer nation's, and more in a nation's own top flight than
        /// its second tier.
        /// </summary>
        public static long Value(Player player, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            if (player == null) throw new System.ArgumentNullException(nameof(player));
            if (cfg == null) throw new System.ArgumentNullException(nameof(cfg));

            MarketBalance m = cfg.Market;

            int valueRating = ValueRating(player, m, cfg.Development);

            int excess = valueRating - m.RatingValueFloor;
            if (excess < 0) excess = 0;

            long value = m.ValueUnitPerRatingCubed * IntPow(excess, m.ValueExponent);

            // Permille multipliers (1000 = no change). Applied in long arithmetic.
            value = value * EliteMultiplierPermille(valueRating, m) / 1000;
            value = value * AgeMultiplierPermille(player.Age, m) / 1000;
            value = value * FormMultiplierPermille(player.Condition.Form, m) / 1000;
            value = value * ContractMultiplierPermille(player.Contract.SeasonsRemaining, m) / 1000;
            value = value * LeagueMultiplierPermille(leagueLevel, economicReputation, cfg.Finance) / 1000;

            value = Round(value, m.ValueRoundingUnit);

            if (value < m.MinValue) value = m.MinValue;
            if (value > m.MaxValue) value = m.MaxValue;
            return value;
        }

        /// <summary>Computes the value (top-tier-nation baseline) and writes it into <see cref="Player.MarketValue"/>.</summary>
        public static void Reprice(Player player, int leagueLevel, BalanceConfig cfg) =>
            player.MarketValue = Value(player, leagueLevel, DefaultEconomicReputation, cfg);

        /// <summary>Computes the value (nation + division aware, R8) and writes it into <see cref="Player.MarketValue"/>.</summary>
        public static void Reprice(Player player, int leagueLevel, int economicReputation, BalanceConfig cfg) =>
            player.MarketValue = Value(player, leagueLevel, economicReputation, cfg);

        // ----------------------------------------------------------------- internals

        /// <summary>
        /// Overall + a youth-scaled share of the remaining potential headroom, clamped to
        /// [1, ValueRatingCap]. The youth scaling is the same age-growth curve the development
        /// model uses, so the premium fades to nothing exactly as a player stops being able to
        /// improve.
        /// </summary>
        private static int ValueRating(Player player, MarketBalance m, DevelopmentBalance dev)
        {
            int overall = PlayerRating.Overall(player);
            int headroom = player.Development.Potential - overall;
            if (headroom < 0) headroom = 0;

            int youthPermille = AgeCurve.GrowthFactorPermille(player.Age, player.Role, dev); // 1000 young → 0 at peak
            int premium = (int)((long)headroom * youthPermille * m.PotentialWeightPercent / (1000L * 100L));

            int rating = overall + premium;
            if (rating < 1) rating = 1;
            if (rating > m.ValueRatingCap) rating = m.ValueRatingCap;
            return rating;
        }

        /// <summary>
        /// The fat top tail (1/1000): no premium below the elite threshold, then a steep per-point
        /// premium so the rare elite value rating (a top star or a young high-potential phenom)
        /// commands multiples of a merely good player — the MaxValue cap bounds the very tip.
        /// </summary>
        private static int EliteMultiplierPermille(int valueRating, MarketBalance m)
        {
            int over = valueRating - m.EliteValueThreshold;
            if (over <= 0) return 1000;
            return 1000 + over * m.ElitePremiumPermillePerPoint;
        }

        /// <summary>Resale value vs age: flat at the prime, then a gentle floored decline (1/1000).</summary>
        private static int AgeMultiplierPermille(int age, MarketBalance m)
        {
            if (age < m.ValueDeclineOnsetAge) return 1000;
            int mult = 1000 - (age - m.ValueDeclineOnsetAge) * m.ValueDeclinePerMillePerYear;
            return mult < m.ValueAgeFloorPermille ? m.ValueAgeFloorPermille : mult;
        }

        /// <summary>Small swing around neutral form 50 (1/1000): hot form lifts, cold trims.</summary>
        private static int FormMultiplierPermille(int form, MarketBalance m)
        {
            // form ∈ [0,100], neutral 50 → 1000; ±FormValueSwingPermille at the extremes.
            return 1000 + (form - 50) * m.FormValueSwingPermille / 50;
        }

        /// <summary>Contract length (1/1000): an expiring deal discounts the fee; long deals are full value.</summary>
        private static int ContractMultiplierPermille(int seasonsRemaining, MarketBalance m)
        {
            int full = m.ContractFullSeasons > 0 ? m.ContractFullSeasons : 1;
            int s = seasonsRemaining < 0 ? 0 : seasonsRemaining > full ? full : seasonsRemaining;
            int floorP = m.ContractExpiringFloorPermille;
            return floorP + s * (1000 - floorP) / full;
        }

        /// <summary>
        /// League level (1/1000): reuses <see cref="FinanceModel.NationDivisionMultiplierPermille"/>
        /// (task #1) rather than a separate division-only curve, so a player's league discount is
        /// nation-aware for free and always agrees with his club's income discount.
        /// </summary>
        private static int LeagueMultiplierPermille(int leagueLevel, int economicReputation, FinanceBalance f)
        {
            int level = leagueLevel < 1 ? 1 : leagueLevel;
            return FinanceModel.NationDivisionMultiplierPermille(economicReputation, level, f);
        }

        /// <summary>Deterministic integer power (no Math.Pow). Exponent ≥ 0.</summary>
        private static long IntPow(long value, int exponent)
        {
            long result = 1;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }

        /// <summary>Rounds to the nearest positive multiple of <paramref name="unit"/>.</summary>
        private static long Round(long value, long unit)
        {
            if (unit <= 1) return value;
            return (value + unit / 2) / unit * unit;
        }
    }
}
