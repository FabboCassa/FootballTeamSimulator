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
        /// <summary>
        /// Gate receipts for one home match: capacity × attendance share × ticket price, scaled by
        /// the club's nation × division wealth (<see cref="NationDivisionMultiplierPermille"/>) and
        /// by the club's own <see cref="Domain.Club.Stature"/> (<see cref="StatureMultiplierPermille"/>,
        /// R4) — the intra-league wealth spread, since nation × division is identical for every
        /// club in a league.
        /// </summary>
        public static long GateReceipts(Club club, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            long capacity = FacilityEffects.StadiumCapacity(club.Facilities.Stadium, f);
            long attendance = capacity * f.AverageAttendancePercent / 100;
            long ticket = f.TicketPriceTopFlight
                          * NationDivisionMultiplierPermille(economicReputation, leagueLevel, f)
                          / 1000;
            long gate = attendance * ticket;
            return gate * StatureMultiplierPermille(club.Stature, f) / 1000;
        }

        /// <summary>
        /// Weekly sponsor income: a base lifted by stadium tier, scaled by the club's nation ×
        /// division wealth (<see cref="NationDivisionMultiplierPermille"/>) and by the club's own
        /// <see cref="Domain.Club.Stature"/> (<see cref="StatureMultiplierPermille"/>, R4).
        /// </summary>
        public static long WeeklySponsor(Club club, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            int tierAboveOne = club.Facilities.Stadium - 1;
            if (tierAboveOne < 0) tierAboveOne = 0;
            long sponsor = f.SponsorWeeklyTopFlight + tierAboveOne * f.SponsorWeeklyPerStadiumTier;
            sponsor = sponsor
                      * NationDivisionMultiplierPermille(economicReputation, leagueLevel, f)
                      / 1000;
            return sponsor * StatureMultiplierPermille(club.Stature, f) / 1000;
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
        /// to the wooden-spoon prize (last position), scaled by the club's nation × division wealth
        /// (<see cref="NationDivisionMultiplierPermille"/>) and then by its own
        /// <see cref="Domain.Club.Stature"/> (<see cref="PrizeStatureMultiplierPermille"/>, R6) — a
        /// club's prestige lifts its prize on top of whatever position it actually finishes in (real
        /// competitions pay coefficient/prestige bonuses alongside pure sporting result).
        /// <paramref name="position"/> is 1-based; <paramref name="clubCount"/> is the division size.
        /// </summary>
        public static long PrizeMoney(int position, int clubCount, int leagueLevel, int economicReputation, int stature, BalanceConfig cfg)
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

            prize = prize
                    * NationDivisionMultiplierPermille(economicReputation, leagueLevel, f)
                    / 1000;
            return prize * PrizeStatureMultiplierPermille(stature, f) / 1000;
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

        // ================= Nation & division wealth (R1-R3) =================
        //
        // Gate, sponsor and prize income are all scaled by ONE combined multiplier instead of each
        // carrying its own division-only discount: a nation wealth factor (from EconomicReputation)
        // times a division wealth factor (from the tier), applied identically to every component.
        // Applying the SAME multiplier everywhere is what makes the division ratio (R3) exact and
        // nation-independent by construction — nation cancels out of a tier2/tier1 ratio because it
        // multiplies both tiers of the same nation equally.

        /// <summary>
        /// Nation wealth multiplier (1/1000): how rich a nation's football is, from its
        /// EconomicReputation. A convex curve (economicReputation ^ <see cref="FinanceBalance.NationWealthExponent"/>)
        /// so the gap between the big five and the rest is wide while nations close in reputation
        /// stay close in wealth — real football's revenue distribution is far more skewed than its
        /// sporting-quality distribution. EconomicReputation 100 (England, by default) maps to
        /// exactly 1000 (no discount, the pre-existing tier-1 baseline); lower reputations scale down
        /// from there, floored so no nation earns literally nothing.
        /// </summary>
        public static int NationMultiplierPermille(int economicReputation, FinanceBalance f)
        {
            int rep = economicReputation;
            if (rep < 0) rep = 0;
            if (rep > 100) rep = 100;

            long numerator = IntPow(rep, f.NationWealthExponent) * 1000;
            long denominator = IntPow(100, f.NationWealthExponent);
            int mult = denominator <= 0 ? 1000 : (int)(numerator / denominator);
            return mult < f.NationWealthFloorPermille ? f.NationWealthFloorPermille : mult;
        }

        /// <summary>
        /// Division wealth multiplier (1/1000): each tier below the top flight earns a fixed share
        /// (<see cref="FinanceBalance.DivisionWealthDecayPermille"/>) of the one above it, compounding
        /// — a real second tier earns 10-25% of the top flight (this spec compresses it to ~35%, and
        /// ~12% for a third tier at the default decay), floored so a deep pyramid never earns nothing.
        /// </summary>
        public static int DivisionMultiplierPermille(int tier, FinanceBalance f)
        {
            int steps = tier < 1 ? 0 : tier - 1;
            long numerator = IntPow(f.DivisionWealthDecayPermille, steps) * 1000;
            long denominator = IntPow(1000, steps);
            int mult = denominator <= 0 ? 1000 : (int)(numerator / denominator);
            return mult < f.DivisionWealthFloorPermille ? f.DivisionWealthFloorPermille : mult;
        }

        /// <summary>Combined nation × division multiplier (1/1000) that gate, sponsor and prize income all scale by.</summary>
        public static int NationDivisionMultiplierPermille(int economicReputation, int tier, FinanceBalance f)
            => NationMultiplierPermille(economicReputation, f) * DivisionMultiplierPermille(tier, f) / 1000;

        // ================= Club stature (R4: intra-league wealth spread) =================

        /// <summary>
        /// The intra-league wealth-spread multiplier (1/1000) a club's persistent
        /// <see cref="Domain.Club.Stature"/> earns on its own gate (<see cref="GateReceipts"/>) and
        /// sponsor (<see cref="WeeklySponsor"/>) income (task: club stature, R4) — a convex curve
        /// (stature/100)^<see cref="FinanceBalance.StatureMultiplierExponent"/> from
        /// <see cref="FinanceBalance.StatureMultiplierFloorPermille"/> at stature 0 to
        /// <see cref="FinanceBalance.StatureMultiplierCeilingPermille"/> at stature 100 — the SAME
        /// shape as <see cref="NationMultiplierPermille"/>, deliberately: gate/sponsor already scale
        /// with a club's strength-driven facility tier, which in practice clusters many clubs at the
        /// same (capped) tier, so stature needs to differentiate revenue across the WHOLE league on
        /// its own for the richest/poorest-of-mean bands (R4) to land, not just nudge the very top.
        /// Applied ON TOP of (multiplicatively with) nation × division wealth inside GateReceipts and
        /// WeeklySponsor themselves; cancels out of the tier2/tier1 division ratio (R3) exactly as
        /// nation × division wealth does. <see cref="PrizeMoney"/> uses the SAME ramp shape but its
        /// own, much gentler floor/ceiling (<see cref="PrizeStatureMultiplierPermille"/>) since
        /// position already captures most of a club's stature-driven quality there.
        /// </summary>
        public static int StatureMultiplierPermille(int stature, FinanceBalance f)
            => RampMultiplierPermille(stature, f.StatureMultiplierFloorPermille, f.StatureMultiplierCeilingPermille, f.StatureMultiplierExponent);

        /// <summary>
        /// The prize-money stature multiplier (1/1000, R6): a much gentler version of
        /// <see cref="StatureMultiplierPermille"/> — floor 1000 (no discount; finishing position
        /// already drives the bulk of prize money) rising to a modest ceiling at stature 100, same
        /// convex ramp shape/exponent so only the very top of the stature ladder is affected.
        /// </summary>
        public static int PrizeStatureMultiplierPermille(int stature, FinanceBalance f)
            => RampMultiplierPermille(stature, f.PrizeStatureMultiplierFloorPermille, f.PrizeStatureMultiplierCeilingPermille, f.StatureMultiplierExponent);

        /// <summary>
        /// Permille ramp (stature/100)^exponent from <paramref name="floorPermille"/> to
        /// <paramref name="ceilingPermille"/>, computed by dividing back down to permille scale on
        /// EVERY multiply step (never IntPow(s, exponent) * 1000 / IntPow(100, exponent) - that
        /// overflows long at the double-digit exponents needed to keep the ramp near-flat for most
        /// of a real generated league and only pull away right at the top few stature points).
        /// </summary>
        private static int RampMultiplierPermille(int stature, int floorPermille, int ceilingPermille, int exponent)
        {
            int s = stature;
            if (s < 0) s = 0;
            if (s > 100) s = 100;

            long rampPermille = 1000;
            for (int i = 0; i < exponent; i++)
                rampPermille = rampPermille * s / 100;

            long mult = floorPermille + (ceilingPermille - floorPermille) * rampPermille / 1000;
            return (int)mult;
        }

        /// <summary>Deterministic integer power (no Math.Pow). Exponent ≥ 0.</summary>
        private static long IntPow(long value, int exponent)
        {
            long result = 1;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }

        // ================= Estimated finances for data-only clubs (R6) =================
        //
        // A data-only club has no fixtures and no table (LeagueDetailLevel.DataOnly), so it never
        // earns a real gate receipt or a real prize. Its revenue is instead ESTIMATED from what a
        // playable club of the same nation x division x stature would earn over a season, and paid
        // out in equal weekly instalments by FinanceProgressor.AccrueDataOnlyWeek — never as gate or
        // prize income, so Finances.SeasonGateIncome/SeasonPrizeIncome stay at zero for these clubs.

        /// <summary>
        /// The league-average prize money (R6): the mean of <see cref="PrizeMoney"/> across every
        /// final position 1..clubCount, at the given club's OWN stature. For the linear top-to-bottom
        /// prize curve this mean (before the stature multiplier) is exactly the midpoint of the
        /// winner's and wooden-spoon's prize, whatever club ends up where — used by
        /// <see cref="EstimatedAnnualRevenue"/> for a club with no table of its own to read a real
        /// position from.
        /// </summary>
        public static long AveragePrizeMoney(int stature, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            FinanceBalance f = cfg.Finance;
            long avg = (f.PrizeWinnerTopFlight + f.PrizeLastTopFlight) / 2;
            avg = avg * NationDivisionMultiplierPermille(economicReputation, leagueLevel, f) / 1000;
            return avg * PrizeStatureMultiplierPermille(stature, f) / 1000;
        }

        /// <summary>
        /// Home fixtures a club plays in one season of a <paramref name="clubCount"/>-club double
        /// round-robin (every club hosts every other club exactly once) — the same shape
        /// <see cref="Career.FixtureGenerator"/> produces for a playable/background league.
        /// </summary>
        private static int SeasonHomeMatches(int clubCount) => clubCount > 1 ? clubCount - 1 : 0;

        /// <summary>
        /// A data-only club's estimated ANNUAL revenue (R6): what a playable club of the same nation x
        /// division x stature would earn over a season — <see cref="SeasonHomeMatches"/> home fixtures'
        /// worth of <see cref="GateReceipts"/>, a full season's worth of <see cref="WeeklySponsor"/> (one
        /// per round, exactly like the real weekly accrual cadence), plus <see cref="AveragePrizeMoney"/>
        /// standing in for the real per-position prize the club never gets to earn.
        /// </summary>
        public static long EstimatedAnnualRevenue(Club club, int leagueLevel, int economicReputation, int clubCount, BalanceConfig cfg)
        {
            int homeMatches = SeasonHomeMatches(clubCount);
            int seasonWeeks = 2 * homeMatches; // double round-robin: two rounds per opponent

            long gate = GateReceipts(club, leagueLevel, economicReputation, cfg) * homeMatches;
            long sponsor = WeeklySponsor(club, leagueLevel, economicReputation, cfg) * seasonWeeks;
            long prize = AveragePrizeMoney(club.Stature, leagueLevel, economicReputation, cfg);

            return gate + sponsor + prize;
        }

        /// <summary>
        /// One week's share of <see cref="EstimatedAnnualRevenue"/> — what <see cref="FinanceProgressor.AccrueDataOnlyWeek"/>
        /// pays a data-only club each week, matching the weekly cadence every other league books
        /// sponsor income on. Integer division rounds down, same as every other weekly figure here.
        /// </summary>
        public static long EstimatedWeeklyRevenue(Club club, int leagueLevel, int economicReputation, int clubCount, BalanceConfig cfg)
        {
            int seasonWeeks = 2 * SeasonHomeMatches(clubCount);
            if (seasonWeeks <= 0) return 0;
            return EstimatedAnnualRevenue(club, leagueLevel, economicReputation, clubCount, cfg) / seasonWeeks;
        }
    }
}
