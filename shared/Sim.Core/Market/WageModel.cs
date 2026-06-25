using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// Derives a player's weekly wage (task 5.5) from the three drivers the user chose: his
    /// market <b>value</b>, the <b>club he plays for</b> (its league level — already baked into the
    /// value — so a top-flight player out-earns the same ability one division down) and the club's
    /// <b>season results</b> (a standings-based ±swing: a successful season lifts the wage bill via
    /// bonuses/renewals, a poor one trims it).
    ///
    /// PURE and deterministic (integer/long math, NO RNG): a wage is a stable function of its
    /// inputs. Never called by the match engine — opt-in, so golden masters are unaffected. Wages
    /// are the dominant expense in <see cref="FinanceModel"/>; magnitudes live in
    /// <see cref="FinanceBalance"/>.
    /// </summary>
    public static class WageModel
    {
        /// <summary>
        /// Weekly wage from a player's market value and the club's season-result multiplier
        /// (1000 = neutral; <see cref="FinanceModel.ResultPermilleForPosition"/> derives it from
        /// the final/standing position). Value already carries the league level the player plays in.
        /// </summary>
        public static long WeeklyWage(long marketValue, int seasonResultPermille, FinanceBalance cfg)
        {
            if (marketValue < 0) marketValue = 0;
            long divisor = cfg.WageWeeklyValueDivisor > 0 ? cfg.WageWeeklyValueDivisor : 1;
            long baseWage = marketValue / divisor;
            return baseWage * seasonResultPermille / 1000;
        }

        /// <summary>Weekly wage for a player, reading his cached <see cref="Player.MarketValue"/> (host keeps it fresh via the valuation re-price).</summary>
        public static long WeeklyWage(Player player, int seasonResultPermille, FinanceBalance cfg) =>
            WeeklyWage(player.MarketValue, seasonResultPermille, cfg);
    }
}
