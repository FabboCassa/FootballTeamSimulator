using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// Derives a player's weekly wage (task 5.5, extended by task: wages set by the paying club, R7)
    /// from three drivers: his ability-derived market <b>value</b>, the <b>club he plays for</b> (its
    /// wage structure = nation × division × stature, see <see cref="FinanceModel.ClubWageStructurePermille"/>
    /// — a player is paid what the CLUB he plays for can afford, not what his ability would fetch on a
    /// neutral transfer market) and the club's <b>season results</b> (a standings-based ±swing: a
    /// successful season lifts the wage bill via bonuses/renewals, a poor one trims it).
    ///
    /// PURE and deterministic (integer/long math, NO RNG): a wage is a stable function of its
    /// inputs. Never called by the match engine — opt-in, so golden masters are unaffected. Wages
    /// are the dominant expense in <see cref="FinanceModel"/>; magnitudes live in
    /// <see cref="FinanceBalance"/>.
    /// </summary>
    public static class WageModel
    {
        /// <summary>1000 = no club-structure premium/discount — the pre-R7 behavior for callers not yet wired to a real club.</summary>
        private const int NeutralWageStructurePermille = 1000;

        /// <summary>
        /// Weekly wage from a player's value and the club's season-result multiplier (1000 = neutral;
        /// see <see cref="FinanceModel.ResultPermilleForPosition"/>), at a NEUTRAL club wage structure
        /// (1000 = no premium/discount). Kept for callers not yet wired to a real paying club (e.g. the
        /// private-league estimator); <see cref="FinanceModel.WeeklyWageBill"/> uses the full overload below.
        /// </summary>
        public static long WeeklyWage(long marketValue, int seasonResultPermille, FinanceBalance cfg) =>
            WeeklyWage(marketValue, seasonResultPermille, NeutralWageStructurePermille, cfg);

        /// <summary>
        /// Weekly wage from a player's value, the club's season-result multiplier and the PAYING
        /// CLUB's own wage structure (task: wages set by the paying club, R7) — see
        /// <see cref="FinanceModel.ClubWageStructurePermille"/> (nation × division × stature). 1000 =
        /// a neutral (top-flight, full-wealth, median-stature) club; a poorer, lower-division or
        /// lower-stature club pays LESS for the exact same player, a richer one pays MORE — moving to
        /// a richer club raises the demanded wage.
        /// </summary>
        public static long WeeklyWage(long marketValue, int seasonResultPermille, int clubWageStructurePermille, FinanceBalance cfg)
        {
            if (marketValue < 0) marketValue = 0;
            long divisor = cfg.WageWeeklyValueDivisor > 0 ? cfg.WageWeeklyValueDivisor : 1;
            long baseWage = marketValue / divisor;
            long wage = baseWage * seasonResultPermille / 1000;
            return wage * clubWageStructurePermille / 1000;
        }

        /// <summary>Weekly wage for a player at a neutral club structure, reading his cached <see cref="Player.MarketValue"/>.</summary>
        public static long WeeklyWage(Player player, int seasonResultPermille, FinanceBalance cfg) =>
            WeeklyWage(player.MarketValue, seasonResultPermille, cfg);

        /// <summary>Weekly wage for a player at the given club wage structure, reading his cached <see cref="Player.MarketValue"/>.</summary>
        public static long WeeklyWage(Player player, int seasonResultPermille, int clubWageStructurePermille, FinanceBalance cfg) =>
            WeeklyWage(player.MarketValue, seasonResultPermille, clubWageStructurePermille, cfg);
    }
}
