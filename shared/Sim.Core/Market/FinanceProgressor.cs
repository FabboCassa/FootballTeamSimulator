using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Career;

namespace Sim.Core.Market
{
    /// <summary>
    /// Evolves a whole world's finances (task 5.5) — the economic twin of the condition /
    /// development / scouting progressors. Income is booked as it occurs (gate receipts on a home
    /// match, sponsors weekly, prize money at season end) and the wage bill is charged weekly;
    /// the operating balance is then floored at <see cref="FinanceBalance.MinBalance"/> — the board
    /// covers any shortfall, so <b>bankruptcy is impossible</b>. The per-season transfer kitty is
    /// seeded from the resulting finances, so <b>overspending leaves a club unable to sign more</b>
    /// (the 5.5 acceptance).
    ///
    /// PURE and deterministic (integer/long math, NO RNG), order-independent across clubs. Opt-in by
    /// being called: the host decides the cadence (the client wires it onto the same day/week/season
    /// hooks it already uses); the match engine never touches it, so golden masters/replays are
    /// unaffected.
    /// </summary>
    public sealed class FinanceProgressor
    {
        private readonly BalanceConfig _cfg;
        private readonly SeasonBalance _seasonCfg;

        public FinanceProgressor(BalanceConfig? config = null)
        {
            _cfg = config ?? new BalanceConfig();
            _seasonCfg = _cfg.Season;
        }

        /// <summary>
        /// Seeds a fresh world's finances at career creation: a starting stadium tier scaled to each
        /// club's strength (so big clubs begin with big grounds → income scales with size), a starting
        /// cash balance, and an initial transfer budget. Idempotent and deterministic.
        /// </summary>
        public void SeedWorld(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    int strength = BudgetModel.ClubStrength(club);
                    club.Facilities.Stadium = FacilityEffects.SuggestedStadiumTier(strength, club.Stature, _cfg.Finance);
                    club.Finances.Balance = FinanceModel.StartingBalance(league.Division, _cfg);
                    club.Finances.ResetSeasonCounters();
                }
            }

            SeedTransferBudgets(leagues);
        }

        /// <summary>
        /// Seeds EVERY club in the whole world (task: estimated finances, R6): playable, background
        /// AND data-only alike, since every club needs a facility tier and a starting balance whether
        /// or not it plays a real season — a data-only club's <see cref="Domain.Club.Facilities"/> and
        /// starting <see cref="Domain.Finances.Balance"/> feed straight into its estimated weekly
        /// revenue (<see cref="FinanceModel.EstimatedWeeklyRevenue"/>) exactly like a playable club's
        /// real gate/sponsor income.
        /// </summary>
        public void SeedWorld(World world) => SeedWorld(world.AllLeagues());

        /// <summary>Credits the home club of every fixture played today its gate receipts.</summary>
        public void AccrueMatchday(IReadOnlyList<League> leagues, IReadOnlyList<MatchOutcome> outcomes)
        {
            foreach (MatchOutcome outcome in outcomes)
                CreditGate(leagues, outcome.Fixture.HomeClubId);
        }

        /// <summary>
        /// Credits gate receipts from already-resolved fixtures directly (task: estimated finances,
        /// R6) — used for BACKGROUND leagues, whose matches are resolved by
        /// <see cref="Career.QuickResultResolver"/> and never produce a <see cref="MatchOutcome"/>
        /// (no minute-by-minute engine runs for them, so there is no report to wrap).
        /// </summary>
        public void AccrueMatchday(IReadOnlyList<League> leagues, IReadOnlyList<Fixture> playedFixtures)
        {
            foreach (Fixture fixture in playedFixtures)
                CreditGate(leagues, fixture.HomeClubId);
        }

        private void CreditGate(IReadOnlyList<League> leagues, int homeClubId)
        {
            if (!FindClub(leagues, homeClubId, out Club? home, out League? homeLeague))
                return;

            long gate = FinanceModel.GateReceipts(home!, homeLeague!.Division, homeLeague.EconomicReputation, _cfg);
            home!.Finances.Balance += gate;
            home.Finances.SeasonGateIncome += gate;
        }

        /// <summary>
        /// Books one week of ESTIMATED revenue for every DATA-ONLY club (task: estimated finances,
        /// R6). Unlike <see cref="AccrueMatchday(IReadOnlyList{League},IReadOnlyList{MatchOutcome})"/>/
        /// <see cref="AccrueWeek"/> this needs no fixtures or table — a data-only league has neither —
        /// so it pays a flat weekly instalment of <see cref="FinanceModel.EstimatedWeeklyRevenue"/>
        /// straight onto the balance and into <see cref="Domain.Finances.SeasonEstimatedIncome"/>,
        /// leaving SeasonGateIncome/SeasonPrizeIncome at zero (no games were played, so there is no
        /// real gate or prize to book) and never touching wages (no wage model runs for these clubs).
        /// </summary>
        public void AccrueDataOnlyWeek(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
            {
                int clubCount = league.Clubs.Count;
                foreach (Club club in league.Clubs)
                {
                    long revenue = FinanceModel.EstimatedWeeklyRevenue(club, league.Division, league.EconomicReputation, clubCount, _cfg);
                    club.Finances.Balance += revenue;
                    club.Finances.SeasonEstimatedIncome += revenue;
                }
            }
        }

        /// <summary>
        /// Books one week of sponsor income and wages for every club. The wage bill is scaled by the
        /// club's current standing (success lifts wages); the balance is floored afterwards so the
        /// board absorbs any operating loss (no bankruptcy).
        /// </summary>
        public void AccrueWeek(IReadOnlyList<League> leagues, Season season)
        {
            foreach (League league in leagues)
            {
                Dictionary<int, int> positions = Positions(league, season);

                foreach (Club club in league.Clubs)
                {
                    int position = positions.TryGetValue(club.Id, out int p) ? p : league.Clubs.Count;
                    int resultPermille = FinanceModel.ResultPermilleForPosition(position, league.Clubs.Count, _cfg);

                    long sponsor = FinanceModel.WeeklySponsor(club, league.Division, league.EconomicReputation, _cfg);
                    long wages = FinanceModel.WeeklyWageBill(
                        club, resultPermille, league.Division, league.EconomicReputation, _cfg);

                    club.Finances.Balance += sponsor - wages;
                    club.Finances.SeasonSponsorIncome += sponsor;
                    club.Finances.SeasonWageExpense += wages;

                    if (club.Finances.Balance < _cfg.Finance.MinBalance)
                        club.Finances.Balance = _cfg.Finance.MinBalance; // board covers the shortfall
                }
            }
        }

        /// <summary>
        /// Awards prize money by final standings (call at season end, before rollover). Each club is
        /// credited per its finishing position in its division.
        /// </summary>
        public void AwardPrizeMoney(IReadOnlyList<League> leagues, Season season)
        {
            foreach (League league in leagues)
            {
                List<LeagueTableRow> table = LeagueTable.Compute(league, season, _seasonCfg);
                for (int i = 0; i < table.Count; i++)
                {
                    Club? club = league.FindClub(table[i].ClubId);
                    if (club == null) continue;

                    long prize = FinanceModel.PrizeMoney(i + 1, table.Count, league.Division, league.EconomicReputation, club.Stature, _cfg);
                    club.Finances.Balance += prize;
                    club.Finances.SeasonPrizeIncome += prize;
                }
            }
        }

        /// <summary>
        /// Seeds every club's <see cref="Club.TransferBudget"/> from its finances (call at season
        /// start). Replaces the 5.2 strength-based <see cref="BudgetModel"/> seed in the live path.
        /// </summary>
        public void SeedTransferBudgets(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    club.TransferBudget = FinanceModel.SeasonTransferBudget(club, league.Division, league.EconomicReputation, _cfg);
        }

        /// <summary>Clears every club's season-to-date income/expense counters (call at rollover).</summary>
        public static void ResetSeasonCounters(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    club.Finances.ResetSeasonCounters();
        }

        // ----------------------------------------------------------------- internals

        /// <summary>Maps each club id to its 1-based position in its league's current table.</summary>
        private Dictionary<int, int> Positions(League league, Season season)
        {
            List<LeagueTableRow> table = LeagueTable.Compute(league, season, _seasonCfg);
            var positions = new Dictionary<int, int>(table.Count);
            for (int i = 0; i < table.Count; i++)
                positions[table[i].ClubId] = i + 1;
            return positions;
        }

        private static bool FindClub(IReadOnlyList<League> leagues, int clubId, out Club? club, out League? foundLeague)
        {
            foreach (League league in leagues)
            {
                Club? found = league.FindClub(clubId);
                if (found != null)
                {
                    club = found;
                    foundLeague = league;
                    return true;
                }
            }

            club = null;
            foundLeague = null;
            return false;
        }
    }
}
