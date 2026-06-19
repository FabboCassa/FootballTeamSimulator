using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Development
{
    /// <summary>
    /// Evolves a whole world's development one week at a time with the age- and
    /// modifier-aware model (task 4.4) — the ageing superset of the 4.3
    /// <see cref="TrainingProgressor"/>. Every club still develops: one present in
    /// <paramref name="plans"/> uses its chosen <see cref="TrainingPlan"/> (the user club),
    /// every other club trains the AI default (<see cref="TrainingPlan.Balanced"/>).
    ///
    /// Each player's per-week <see cref="DevelopmentContext"/> (minutes / facility /
    /// performance) comes from <c>contexts</c> keyed by player id; a player absent from it
    /// gets <see cref="DevelopmentContext.Neutral"/> (full minutes, neutral facility &amp;
    /// performance → growth scaled by age alone), which is what AI clubs and any un-tracked
    /// player get until the host wires real minutes/facilities (the 4.4 client follow-up).
    ///
    /// Deterministic and order-independent: each club's RNG is derived from
    /// (worldSeed, week, clubId), mirroring <see cref="TrainingProgressor"/> and
    /// <see cref="Condition.ConditionProgressor"/>. Opt-in by being called; a host that
    /// never calls it leaves attributes untouched, so match golden masters are unaffected.
    /// </summary>
    public sealed class DevelopmentProgressor
    {
        /// <summary>Golden-ratio odd constant, decorrelates per-club development seeds.</summary>
        private const ulong ClubSeedMix = 0x9E3779B97F4A7C15UL;

        private static readonly TrainingPlan DefaultPlan = TrainingPlan.Balanced();

        private readonly DevelopmentBalance _cfg;

        public DevelopmentProgressor(DevelopmentBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>Develops every club in the world for one week.</summary>
        public void EvolveWeek(
            IReadOnlyList<League> leagues,
            IReadOnlyDictionary<int, TrainingPlan>? plans,
            IReadOnlyDictionary<int, DevelopmentContext>? contexts,
            ulong worldSeed,
            int week)
        {
            foreach (League league in leagues)
            {
                foreach (Club club in league.Clubs)
                {
                    TrainingPlan plan = ResolvePlan(plans, club.Id);
                    var rng = new Pcg32(worldSeed ^ ((ulong)club.Id * ClubSeedMix), (ulong)week);

                    foreach (Player player in club.Squad.Players)
                    {
                        DevelopmentContext ctx = ResolveContext(contexts, player.Id);
                        DevelopmentModel.ApplyDevelopmentWeek(
                            player, plan.TeamFocus, plan.FocusFor(player.Id), ctx, rng, _cfg);
                    }
                }
            }
        }

        private static TrainingPlan ResolvePlan(IReadOnlyDictionary<int, TrainingPlan>? plans, int clubId)
        {
            if (plans != null && plans.TryGetValue(clubId, out TrainingPlan? plan) && plan != null)
                return plan;

            return DefaultPlan;
        }

        private DevelopmentContext ResolveContext(IReadOnlyDictionary<int, DevelopmentContext>? contexts, int playerId)
        {
            if (contexts != null && contexts.TryGetValue(playerId, out DevelopmentContext ctx))
                return ctx;

            return DevelopmentContext.Neutral(_cfg);
        }
    }
}
