using System.Collections.Generic;
using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Development
{
    /// <summary>
    /// Evolves a whole world's player development one training week at a time (task 4.3).
    /// Every club trains: a club present in <paramref name="plans"/> uses its chosen
    /// <see cref="TrainingPlan"/> (the user club), every other club trains the AI default
    /// (<see cref="TrainingPlan.Balanced"/>) — so the user controls only his own training
    /// while the whole world still improves or declines around him.
    ///
    /// Deterministic and order-independent: each club's RNG is derived from
    /// (worldSeed, week, clubId), mirroring <see cref="Condition.ConditionProgressor"/>,
    /// so the outcome never depends on the order clubs are visited in. Opt-in by being
    /// called — a host that never calls it leaves attributes untouched, so existing
    /// match golden masters are unaffected.
    /// </summary>
    public sealed class TrainingProgressor
    {
        /// <summary>Golden-ratio odd constant, decorrelates per-club training seeds.</summary>
        private const ulong ClubSeedMix = 0x9E3779B97F4A7C15UL;

        private static readonly TrainingPlan DefaultPlan = TrainingPlan.Balanced();

        private readonly DevelopmentBalance _cfg;

        public TrainingProgressor(DevelopmentBalance cfg)
        {
            _cfg = cfg;
        }

        /// <summary>
        /// Trains every club in the world for one week. <paramref name="plans"/> maps a
        /// club id to its plan; clubs absent from it train the balanced AI default.
        /// </summary>
        public void EvolveWeek(
            IReadOnlyList<League> leagues,
            IReadOnlyDictionary<int, TrainingPlan>? plans,
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
                        TrainingModel.ApplyTrainingWeek(
                            player, plan.TeamFocus, plan.FocusFor(player.Id), rng, _cfg);
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
    }
}
