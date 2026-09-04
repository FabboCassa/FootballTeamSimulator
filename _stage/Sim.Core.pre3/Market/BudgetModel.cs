using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Match;

namespace Sim.Core.Market
{
    /// <summary>
    /// Seeds club transfer budgets (task 5.2) — a minimal-but-real spending limit until full
    /// finances land at 5.5. A club's kitty scales convexly with its squad strength (best-XI
    /// average overall) and is discounted for lower divisions, then floored so even small clubs
    /// can do some business.
    ///
    /// PURE, deterministic and order-independent (NO RNG): a stable function of squad state, so
    /// the host can call it whenever (the client seeds once per season). It OVERWRITES
    /// <see cref="Club.TransferBudget"/>, so call it at season start before the windows, not
    /// mid-window (a sale/purchase adjusts the budget during a window).
    /// </summary>
    public sealed class BudgetModel
    {
        private readonly TransferBalance _cfg;

        public BudgetModel(BalanceConfig cfg)
        {
            _cfg = cfg.Transfer;
        }

        /// <summary>Best-XI average overall — the club's quality, used to size its budget.</summary>
        public static int ClubStrength(Club club)
        {
            Lineup xi = LineupSelector.BestEleven(club);
            if (xi.Slots.Count == 0) return 0;
            long sum = 0;
            foreach (LineupSlot slot in xi.Slots)
                sum += PlayerRating.OverallFor(slot.Player, slot.Role);
            return (int)(sum / xi.Slots.Count);
        }

        /// <summary>The seeded budget for one club at the given division level (1 = top flight).</summary>
        public long BudgetFor(Club club, int leagueLevel)
        {
            int strength = ClubStrength(club);
            int excess = strength - _cfg.BudgetStrengthFloor;
            if (excess < 0) excess = 0;

            long budget = _cfg.BudgetUnitPerStrengthSquared * (excess * (long)excess) / 100;

            int level = leagueLevel < 1 ? 1 : leagueLevel;
            int leagueMult = 1000 - (level - 1) * _cfg.BudgetLeagueDiscountPermille;
            if (leagueMult < _cfg.BudgetLeagueFloorPermille) leagueMult = _cfg.BudgetLeagueFloorPermille;
            budget = budget * leagueMult / 1000;

            if (budget < _cfg.MinBudget) budget = _cfg.MinBudget;
            return budget;
        }

        /// <summary>Seeds every club's <see cref="Club.TransferBudget"/> across every league.</summary>
        public void SeedBudgets(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
                foreach (Club club in league.Clubs)
                    club.TransferBudget = BudgetFor(club, league.Division);
        }
    }
}
