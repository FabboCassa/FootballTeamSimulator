using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// The pure financial calculators (task 5.5): gate receipts, sponsor income, prize money,
    /// the weekly wage bill, the season transfer budget and a starting balance. PURE and
    /// deterministic (integer/long math, NO RNG) — every figure is a stable function of club
    /// state, so the host can book finances on whatever cadence it likes without perturbing any
    /// seeded stream. <see cref="FinanceProgressor"/> orchestrates these over a season; the match
    /// engine never calls them (opt-in → golden masters unaffected).
    ///
    /// Magnitudes live in <see cref="FinanceBalance"/>; the shapes are structural here.
    /// </summary>
    public static class FinanceModel
    {
        /// <summary>Gate receipts for one home match: capacity × attendance share × ticket price (league-scaled).</summary>
        public static long GateReceipts(Club club, int leagueLevel, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            long capacity = FacilityEffects.StadiumCapacity(club.Facilities.Stadium, f);
            long attendance = capacity * f.AverageAttendancePercent / 100;
            long ticket = f.TicketPriceTopFlight
                          * LeagueMultiplierPermille(leagueLevel, f.TicketDivisionDiscountPermille, f.TicketDivisionFloorPermille)
                          / 1000;
            return attendance * ticket;
        }

        /// <summary>Weekly sponsor income: a base lifted by stadium tier, then league-scaled.</summary>
        public static long WeeklySponsor(Club club, int leagueLevel, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            int tierAboveOne = club.Facilities.Stadium - 1;
            if (tierAboveOne < 0) tierAboveOne = 0;
            long sponsor = f.SponsorWeeklyTopFlight + tierAboveOne * f.SponsorWeeklyPerStadiumTier;
            return sponsor
                   * LeagueMultiplierPermille(leagueLevel, f.SponsorDivisionDiscountPermille, f.SponsorDivisionFloorPermille)
                   / 1000;
        }

        /// <summary>The club's weekly wage bill: the sum of every squad player's derived weekly wage.</summary>
        public static long WeeklyWageBill(Club club, int seasonResultPermille, BalanceConfig cfg)
        {
            long bill = 0;
            foreach (Player player in club.Squad.Players)
                bill += WageModel.WeeklyWage(player, seasonResultPermille, cfg.Finance);
            return bill;
        }

        /// <summary>
        /// Prize money for a final league finish: linear from the winner's prize (position 1) down
        /// to the wooden-spoon prize (last position), then league-scaled. <paramref name="position"/>
        /// is 1-based; <paramref name="clubCount"/> is the division size.
        /// </summary>
        public static long PrizeMoney(int position, int clubCount, int leagueLevel, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            if (position < 1) position = 1;
            if (clubCount < 1) clubCount = 1;
            if (position > clubCount) position = clubCount;

            long top = f.PrizeWinnerTopFlight;
            long bottom = f.PrizeLastTopFlight;
            long prize = clubCount <= 1
                ? top
                : top - (top - bottom) * (position - 1) / (clubCount - 1);

            return prize
                   * LeagueMultiplierPermille(leagueLevel, f.PrizeDivisionDiscountPermille, f.PrizeDivisionFloorPermille)
                   / 1000;
        }

        /// <summary>
        /// The wage multiplier (1/1000) a final/standing position earns: the leader earns the ceiling
        /// (success → bonuses), the bottom club the floor, linear between — 1000 sits mid-table.
        /// </summary>
        public static int ResultPermilleForPosition(int position, int clubCount, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            if (clubCount <= 1) return 1000;
            if (position < 1) position = 1;
            if (position > clubCount) position = clubCount;

            int ceil = f.WageResultCeilPermille;
            int floor = f.WageResultFloorPermille;
            // position 1 → ceil, position clubCount → floor.
            return ceil - (ceil - floor) * (position - 1) / (clubCount - 1);
        }

        /// <summary>A fresh club's starting operating cash, scaled down for lower divisions.</summary>
        public static long StartingBalance(int leagueLevel, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            return f.StartingBalanceTopFlight
                   * LeagueMultiplierPermille(leagueLevel, f.StartingBalanceDivisionDiscountPermille, f.StartingBalanceDivisionFloorPermille)
                   / 1000;
        }

        /// <summary>
        /// The season transfer kitty the board backs: a share of current cash reserves plus a flat
        /// board grant (league-scaled), floored. Replaces the 5.2 strength-based seed in the live
        /// path — so a club that has spent its cash gets a smaller kitty (overspending blocks signings).
        /// </summary>
        public static long SeasonTransferBudget(Club club, int leagueLevel, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            long cashShare = club.Finances.Balance * f.TransferBudgetCashPercent / 100;
            if (cashShare < 0) cashShare = 0;

            long grant = f.BoardGrantTopFlight
                         * LeagueMultiplierPermille(leagueLevel, f.BoardGrantDivisionDiscountPermille, f.BoardGrantDivisionFloorPermille)
                         / 1000;

            long budget = cashShare + grant;
            if (budget < f.MinTransferBudget) budget = f.MinTransferBudget;
            return budget;
        }

        /// <summary>League level (1/1000): top flight = full, each lower division discounts, floored.</summary>
        private static int LeagueMultiplierPermille(int leagueLevel, int discountPermille, int floorPermille)
        {
            int level = leagueLevel < 1 ? 1 : leagueLevel;
            int mult = 1000 - (level - 1) * discountPermille;
            return mult < floorPermille ? floorPermille : mult;
        }
    }
}
