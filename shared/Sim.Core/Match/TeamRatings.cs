using Sim.Core.Condition;
using Sim.Core.Config;
using Sim.Core.Domain;

namespace Sim.Core.Match
{
    /// <summary>
    /// Aggregates a lineup into three numbers the engine reasons about.
    /// Players deployed out of role are rated for the slot role, so the penalty is implicit.
    /// </summary>
    public readonly struct TeamRatings
    {
        public readonly double Attack;
        public readonly double Midfield;
        public readonly double Defense;

        private TeamRatings(double attack, double midfield, double defense)
        {
            Attack = attack;
            Midfield = midfield;
            Defense = defense;
        }

        public static TeamRatings From(Lineup lineup) => Build(lineup, null);

        /// <summary>
        /// Condition-aware aggregation (task 4.1): each player's rating is scaled by his
        /// current form/morale/fitness via <see cref="ConditionModel.PerformanceMultiplier"/>.
        /// A squad of neutral players (form/morale neutral, full fitness) yields exactly
        /// the same numbers as <see cref="From(Lineup)"/>, so this is opt-in and identity-safe.
        /// </summary>
        public static TeamRatings FromWithCondition(Lineup lineup, ConditionBalance cfg) => Build(lineup, cfg);

        private static TeamRatings Build(Lineup lineup, ConditionBalance? condition)
        {
            double gk = 0;
            double defSum = 0, midSum = 0, attSum = 0;
            int defCount = 0, midCount = 0, attCount = 0;

            foreach (LineupSlot slot in lineup.Slots)
            {
                // When condition is null this is the integer overall, so the default
                // path is byte-identical to the pre-condition aggregation.
                double rating = PlayerRating.OverallFor(slot.Player, slot.Role);
                if (condition != null)
                    rating *= ConditionModel.PerformanceMultiplier(slot.Player.Condition, condition);

                switch (slot.Role)
                {
                    case PositionRole.Goalkeeper:
                        gk = rating;
                        break;
                    case PositionRole.CentreBack:
                    case PositionRole.FullBack:
                        defSum += rating; defCount++;
                        break;
                    case PositionRole.DefensiveMidfielder:
                    case PositionRole.CentralMidfielder:
                    case PositionRole.AttackingMidfielder:
                        midSum += rating; midCount++;
                        break;
                    default: // Winger, Striker
                        attSum += rating; attCount++;
                        break;
                }
            }

            double defAvg = defCount > 0 ? defSum / defCount : 1;
            double midAvg = midCount > 0 ? midSum / midCount : 1;
            double attAvg = attCount > 0 ? attSum / attCount : 1;

            return new TeamRatings(
                attack: attAvg * 0.7 + midAvg * 0.3,
                midfield: midAvg,
                defense: defAvg * 0.6 + gk * 0.4);
        }

        public TeamRatings Scaled(double factor) =>
            new TeamRatings(Attack * factor, Midfield * factor, Defense * factor);

        /// <summary>Applies independent per-component multipliers (used by the tactics system, task 3.2).</summary>
        public TeamRatings WithMultipliers(double attack, double midfield, double defense) =>
            new TeamRatings(Attack * attack, Midfield * midfield, Defense * defense);
    }
}
