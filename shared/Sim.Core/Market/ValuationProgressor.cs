using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Market
{
    /// <summary>
    /// Re-prices a whole world (task 5.1, the "periodic re-pricing" of ARCHITECTURE.md §4.7):
    /// walks every league and writes each player's <see cref="Player.MarketValue"/> via
    /// <see cref="ValuationModel"/>, using that league's <see cref="League.Division"/> AND
    /// <see cref="League.EconomicReputation"/> as the price level (task: player market value by
    /// league, R8) — lower divisions and poorer nations discount the fee.
    ///
    /// Pure and deterministic, no RNG and order-independent — valuation is a stable function
    /// of player state, so the cached values never depend on iteration order or any seed.
    /// Opt-in by being called: a host decides the cadence (the client wires a periodic call,
    /// e.g. on the weekly tick, as the 5.1 follow-up); never calling it leaves
    /// <see cref="Player.MarketValue"/> at its stored value, so nothing else is affected.
    /// </summary>
    public sealed class ValuationProgressor
    {
        private readonly BalanceConfig _cfg;

        public ValuationProgressor(BalanceConfig cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Re-prices every player in every club of every league.</summary>
        public void Reprice(IReadOnlyList<League> leagues)
        {
            foreach (League league in leagues)
                Reprice(league);
        }

        /// <summary>
        /// Re-prices every player in one league at its division level and nation economic
        /// reputation (task: player market value by league, R8) — <see cref="League.EconomicReputation"/>
        /// is denormalised from <see cref="Domain.Nation.EconomicReputation"/> at generation time, so
        /// no separate nation/World lookup is needed here.
        /// </summary>
        public void Reprice(League league)
        {
            int level = league.Division;
            int economicReputation = league.EconomicReputation;
            foreach (Club club in league.Clubs)
                foreach (Player player in club.Squad.Players)
                    ValuationModel.Reprice(player, level, economicReputation, _cfg);
        }
    }
}
