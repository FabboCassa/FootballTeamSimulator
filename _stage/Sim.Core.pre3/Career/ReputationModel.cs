using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Market;

namespace Sim.Core.Career
{
    /// <summary>
    /// Coach reputation and club stature (task 5.6). PURE and deterministic — integer math, NO RNG.
    ///   • <see cref="SeasonEndReputationDelta"/> moves <see cref="Coach.Reputation"/> by how a season
    ///     compared to the board objective (over-performance and titles lift it, under-performance
    ///     trims it), scaled DOWN in lower divisions (success in the basement counts for less);
    ///   • <see cref="RequiredReputation"/> maps a club to the reputation a coach needs to be a
    ///     credible candidate for its bench — the gate <see cref="JobMarket"/> uses for offers/hiring.
    /// Opt-in: nothing in the match engine references it → golden masters unaffected.
    /// </summary>
    public sealed class ReputationModel
    {
        private readonly CareerBalance _cfg;

        public ReputationModel(BalanceConfig? config = null)
        {
            _cfg = (config ?? new BalanceConfig()).Career;
        }

        /// <summary>
        /// The reputation change a finished season earns: per position vs the objective, plus a title
        /// bonus for winning the division, scaled by division (top flight = full), capped at
        /// ±<see cref="CareerBalance.MaxReputationDeltaPerSeason"/>.
        /// </summary>
        public int SeasonEndReputationDelta(int actualPosition, int expectedPosition, int division)
        {
            int gap = expectedPosition - actualPosition; // >0 = better than expected
            int delta = gap * _cfg.ReputationPerPositionVsObjective;
            if (actualPosition == 1) delta += _cfg.ReputationTitleBonus;

            int scalePermille = DivisionScalePermille(division);
            delta = delta * scalePermille / 1000;

            int max = _cfg.MaxReputationDeltaPerSeason;
            if (delta > max) delta = max;
            if (delta < -max) delta = -max;
            return delta;
        }

        /// <summary>Applies a season-end reputation delta to the coach (clamped to [0,100] by Coach).</summary>
        public void ApplySeasonEnd(Coach coach, int actualPosition, int expectedPosition, int division)
        {
            coach.Reputation += SeasonEndReputationDelta(actualPosition, expectedPosition, division);
        }

        /// <summary>
        /// The reputation a coach needs to be a credible candidate for this club: scales with the
        /// club's squad strength (a stronger squad = a more prestigious job) and drops for each
        /// division below the top flight. Clamped to [0, 100]. This is the club's "stature" on the
        /// reputation scale, used both to gate offers to the user and to rank vacant jobs.
        /// </summary>
        public int RequiredReputation(Club club, int division)
        {
            int strength = BudgetModel.ClubStrength(club);
            int excess = strength - _cfg.StatureStrengthFloor;
            if (excess < 0) excess = 0;

            int req = excess * _cfg.RequiredReputationPerStrengthPermille / 1000;
            req -= (division - 1) * _cfg.RequiredReputationDivisionDrop;

            if (req < 0) req = 0;
            if (req > 100) req = 100;
            return req;
        }

        /// <summary>The initial reputation an AI coach is seeded with from his club's stature (floored).</summary>
        public int SeedReputation(Club club, int division)
        {
            int req = RequiredReputation(club, division);
            return req < _cfg.SeedReputationFloor ? _cfg.SeedReputationFloor : req;
        }

        private int DivisionScalePermille(int division)
        {
            if (division < 1) division = 1;
            int scale = 1000 - (division - 1) * _cfg.ReputationDivisionScalePermille;
            int floor = _cfg.ReputationDivisionScalePermille; // never below one division-step's worth
            if (scale < floor) scale = floor;
            return scale;
        }
    }
}
