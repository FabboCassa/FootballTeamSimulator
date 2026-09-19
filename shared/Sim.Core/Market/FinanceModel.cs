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

        /// <summary>
        /// The club's weekly wage bill (task: wages set by the paying club, R7): the SUM of what
        /// every player in the squad actually demands from THIS club (<see cref="DemandedWeeklyWage"/>,
        /// the same per-player formula the server uses for a real transfer negotiation) — one formula,
        /// two callers, so a club's aggregate bill is never a separate curve from what an individual
        /// signing would cost it. The R7 median wage/revenue bands are a property of the CALIBRATION
        /// (<see cref="ClubWageStructurePermille"/>'s nation/division/stature factors and
        /// <see cref="FinanceBalance.WageWeeklyValueDivisor"/>) rather than of this method, which does
        /// no target-share anchoring of its own.
        /// </summary>
        public static long WeeklyWageBill(Club club, int seasonResultPermille, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            long total = 0;
            foreach (Player player in club.Squad.Players)
                total += DemandedWeeklyWage(player, club, seasonResultPermille, leagueLevel, economicReputation, cfg);
            return total;
        }

        /// <summary>The division's average final-position prize (top and bottom average exactly, since
        /// <see cref="PrizeMoney"/> is linear in position) — used to amortise a season-end lump sum into
        /// <see cref="WeeklyWageBill"/>'s weekly revenue estimate without waiting for a final table.</summary>
        private static long AverageSeasonPrize(int clubCount, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            if (clubCount < 1) clubCount = 1;
            long top = PrizeMoney(1, clubCount, leagueLevel, economicReputation, cfg);
            long bottom = PrizeMoney(clubCount, clubCount, leagueLevel, economicReputation, cfg);
            return (top + bottom) / 2;
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

        /// <summary>
        /// The wage a specific player would be paid at a specific club (task: wages set by the paying
        /// club, R7) — the per-player building block of <see cref="WeeklyWageBill"/>, exposed so the
        /// client/negotiation screen can preview "what would he demand at THIS club" (e.g. during a
        /// transfer) without re-deriving the ability-value + wage-structure math itself. Uses
        /// <see cref="WageAbilityValue"/>, NOT the market's <see cref="ValuationModel"/> transfer-fee
        /// curve — see that method's remarks for why.
        /// </summary>
        public static long DemandedWeeklyWage(Player player, Club club, int seasonResultPermille, int leagueLevel, int economicReputation, BalanceConfig cfg)
        {
            int structure = ClubWageStructurePermille(club.Stature, leagueLevel, economicReputation, cfg.Finance);
            long abilityValue = WageAbilityValue(player, cfg.Finance);
            return WageModel.WeeklyWage(abilityValue, seasonResultPermille, structure, cfg.Finance);
        }

        /// <summary>
        /// A player's wage-side ability value (task: wages set by the paying club, R7): LINEAR in
        /// (overall − <see cref="FinanceBalance.WageValueRatingFloor"/>) plus a flat
        /// <see cref="FinanceBalance.WageLivingWageValue"/> EVERY player gets regardless of ability —
        /// DELIBERATELY milder than the market's <see cref="ValuationModel"/> transfer-fee curve (cubic,
        /// floored at <see cref="MarketBalance.RatingValueFloor"/> ≈ 30) — a real footballer's WAGE
        /// reflects his CURRENT ability to play, not a discounted resale fee, so even a modest player
        /// still draws a real, living wage where the market's fee curve would price him near zero. This
        /// matters structurally: a real generated squad's overall rating already falls with division
        /// (<see cref="Generation.LeagueGenerator"/>'s baseline), and reusing the market's fee curve for
        /// wages compounds that fall with the fee curve's own floor/cubic steepness — the two effects
        /// together crash a real division's total wage demand far faster than its revenue falls, making
        /// the R7 median wage/revenue ratio (which must RISE tier over tier) unreachable together with
        /// "the same player must earn ≥2x at a tier-1 club vs tier-2" (which bounds how much the wage
        /// STRUCTURE alone may discount a lower division — see <see cref="WageDivisionMultiplierPermille"/>).
        /// The flat living-wage term is what keeps the ratio rising tier over tier without touching that
        /// bound: it is the SAME absolute amount at every tier, so it matters proportionally more for a
        /// lower division's naturally smaller ability value — exactly a real living wage's effect on a
        /// smaller squad budget.
        /// </summary>
        public static long WageAbilityValue(Player player, FinanceBalance f)
        {
            int overall = PlayerRating.Overall(player);
            int excess = overall - f.WageValueRatingFloor;
            if (excess < 0) excess = 0;
            return f.WageValueUnitPerRating * excess + f.WageLivingWageValue;
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
            long mult = f.StatureMultiplierFloorPermille
                        + (f.StatureMultiplierCeilingPermille - f.StatureMultiplierFloorPermille)
                        * ConvexRampPermille(stature, f.StatureMultiplierExponent) / 1000;
            return (int)mult;
        }

        /// <summary>
        /// Shared convex ramp (s/100)^exponent, in permille, computed by dividing back down to
        /// permille scale on EVERY multiply step (never IntPow(s, exponent) * 1000 / IntPow(100,
        /// exponent) - that overflows long at the double-digit exponents needed to keep the ramp
        /// near-flat for most of a real generated league and only pull away right at the top few
        /// stature points). Shared by <see cref="StatureMultiplierPermille"/> (revenue) and
        /// <see cref="WageStatureMultiplierPermille"/> (wage structure) — using the SAME shape for
        /// both is what keeps a club's wage/revenue ratio independent of its own stature draw (see
        /// the wage-structure remarks below).
        /// </summary>
        private static long ConvexRampPermille(int stature, int exponent)
        {
            int s = stature < 0 ? 0 : stature > 100 ? 100 : stature;
            long ramp = 1000;
            for (int i = 0; i < exponent; i++)
                ramp = ramp * s / 100;
            return ramp;
        }

        /// <summary>Deterministic integer power (no Math.Pow). Exponent ≥ 0.</summary>
        private static long IntPow(long value, int exponent)
        {
            long result = 1;
            for (int i = 0; i < exponent; i++) result *= value;
            return result;
        }

        // ================= Club wage structure (R7: wages set by the paying club) =================
        //
        // ClubWageStructurePermille answers BOTH "what would the SAME player (fixed ability) earn if
        // he moved to a poorer/richer club" (DemandedWeeklyWage, the transfer-negotiation preview)
        // AND, summed over a real squad, "what is THIS club's real wage bill" (WeeklyWageBill) - one
        // formula, two callers (the user decision behind this task). Three factors:
        //   - nation wealth (NationMultiplierPermille) — the SAME curve revenue uses, so it cancels
        //     out of the wage/revenue ratio for any two clubs in the same nation;
        //   - WageDivisionMultiplierPermille — a FLATTER decay than revenue's own DivisionWealthDecayPermille
        //     (see WageDivisionWealthDecayPermille's remarks) — this is what makes the R7 median
        //     wage/revenue ratio climb tier over tier (55-70% / 72-88% / 78-92%): wages hold up better
        //     than revenue does going down the pyramid;
        //   - WageStatureMultiplierPermille — the SAME convex shape (exponent) as the revenue-side
        //     StatureMultiplierPermille, with its floor:ceiling RATIO locked to match (see that
        //     method's remarks), so a club's wage structure and its own revenue move together as
        //     stature varies, and the wage/revenue ratio does not depend on which stature a club
        //     happens to draw — only squad-value noise (a real, and desired, second driver of a
        //     club's own bill) and the tier/nation factors above still vary the ratio club to club.

        /// <summary>
        /// The paying club's wage structure multiplier (1/1000): nation wealth ×
        /// <see cref="WageDivisionMultiplierPermille"/> × the club's own <see cref="Domain.Club.Stature"/>
        /// (<see cref="WageStatureMultiplierPermille"/>). 1000 = a neutral (top-flight, full nation
        /// wealth, median-stature) club. See the remarks above.
        /// </summary>
        public static int ClubWageStructurePermille(int stature, int leagueLevel, int economicReputation, FinanceBalance f)
        {
            long mult = (long)NationMultiplierPermille(economicReputation, f) * WageDivisionMultiplierPermille(leagueLevel, f) / 1000;
            mult = mult * WageStatureMultiplierPermille(stature, f) / 1000;
            return (int)mult;
        }

        /// <summary>
        /// Division factor (1/1000) for the club wage structure, one entry per tier (tier 1 is always
        /// 1000 - a neutral top-flight club - by construction of <see cref="FinanceBalance.WageDivisionWealthPermille"/>);
        /// a tier beyond the configured list repeats the deepest entry. An explicit per-tier table
        /// rather than a single decay/floor formula (contrast <see cref="DivisionMultiplierPermille"/>,
        /// revenue's own) is DELIBERATE: tier 2's entry is bound tightly from above by the "same player
        /// must earn ≥2x at a tier-1 club vs tier-2" requirement (so it decays), while tier 3's needs to
        /// sit relatively HIGHER (not a compounded further decay of tier 2's) for the R7 median
        /// wage/revenue ratio to keep rising into the tier-3 band — two independent constraints a single
        /// geometric curve cannot satisfy at once.
        /// </summary>
        public static int WageDivisionMultiplierPermille(int tier, FinanceBalance f)
        {
            int[] mults = f.WageDivisionWealthPermille;
            if (mults == null || mults.Length == 0) return 1000;
            int index = tier - 1;
            if (index < 0) index = 0;
            if (index >= mults.Length) index = mults.Length - 1;
            return mults[index];
        }

        /// <summary>
        /// Wage multiplier (1/1000) from a club's <see cref="Domain.Club.Stature"/>: the SAME convex
        /// shape (<see cref="FinanceBalance.StatureMultiplierExponent"/>) as the revenue-side
        /// <see cref="StatureMultiplierPermille"/>, with its own floor/ceiling — see the wage-structure
        /// remarks above for why this cancels a club's own stature out of its wage/revenue ratio.
        /// </summary>
        public static int WageStatureMultiplierPermille(int stature, FinanceBalance f)
        {
            long mult = f.WageStatureFloorPermille
                        + (f.WageStatureCeilingPermille - f.WageStatureFloorPermille)
                        * ConvexRampPermille(stature, f.StatureMultiplierExponent) / 1000;
            return (int)mult;
        }
    }
}
