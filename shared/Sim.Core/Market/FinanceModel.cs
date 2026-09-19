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
        /// to the wooden-spoon prize (last position), then scaled by the club's nation × division
        /// wealth (<see cref="NationDivisionMultiplierPermille"/>). <paramref name="position"/> is
        /// 1-based; <paramref name="clubCount"/> is the division size.
        /// </summary>
        public static long PrizeMoney(int position, int clubCount, int leagueLevel, int economicReputation, BalanceConfig cfg)
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
                   * NationDivisionMultiplierPermille(economicReputation, leagueLevel, f)
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
        /// WeeklySponsor themselves, so it never touches PrizeMoney (position already drives that) and
        /// cancels out of the tier2/tier1 division ratio (R3) exactly as nation × division wealth does.
        /// </summary>
        public static int StatureMultiplierPermille(int stature, FinanceBalance f)
        {
            int s = stature;
            if (s < 0) s = 0;
            if (s > 100) s = 100;

            // Permille ramp (s/100)^exponent, computed by dividing back down to permille scale on
            // EVERY multiply step (never IntPow(s, exponent) * 1000 / IntPow(100, exponent) - that
            // overflows long at the double-digit exponents needed to keep the ramp near-flat for
            // most of a real generated league and only pull away right at the top few stature
            // points, which is what the R4 richest/poorest bands need on real generated data).
            long rampPermille = 1000;
            for (int i = 0; i < f.StatureMultiplierExponent; i++)
                rampPermille = rampPermille * s / 100;

            long mult = f.StatureMultiplierFloorPermille
                        + (f.StatureMultiplierCeilingPermille - f.StatureMultiplierFloorPermille) * rampPermille / 1000;
            return (int)mult;
        }

        /// <summary>Deterministic integer power (no Math.Pow). Exponent ≥ 0.</summary>
        private static long IntPow(long value, int exponent)
        {
            long result = 1;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }
    }
}
