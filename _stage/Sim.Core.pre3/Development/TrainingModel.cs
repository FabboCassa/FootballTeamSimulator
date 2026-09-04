using Sim.Core.Config;
using Sim.Core.Domain;
using Sim.Core.Random;

namespace Sim.Core.Development
{
    /// <summary>
    /// The training-driven development model (task 4.3). Pure and deterministic:
    /// integer math plus a single seeded RNG draw per skill (no transcendental
    /// functions), so it runs identically on .NET / Mono / IL2CPP. The host owns the
    /// stored attributes and the weekly call schedule (see <see cref="TrainingProgressor"/>).
    ///
    /// A week of training biases which skills a player works on (team focus + optional
    /// individual focus → a per-skill weight). Growth is gated by the player's hidden
    /// <see cref="PlayerDevelopment.Potential"/>:
    ///   - while overall &lt; potential the player improves, fastest on the focused
    ///     skills and faster the more headroom he has;
    ///   - once overall reaches potential he drifts gently downward (ageing/maintenance),
    ///     but never more than <see cref="DevelopmentBalance.DeclineFloorPoints"/> below
    ///     potential, and the skills he is drilling are protected — so the world improves
    ///     OR declines, capped, never chaotic (ARCHITECTURE.md §4.4).
    ///
    /// Exactly <see cref="PlayerAttributes.SkillCount"/> RNG draws per player per week,
    /// regardless of regime or focus, so changing the focus re-thresholds the SAME random
    /// rolls — isolating the effect of training in any A/B harness.
    /// </summary>
    public static class TrainingModel
    {
        // Skill order (matches PlayerAttributes / PlayerRating):
        // 0 Pace, 1 Strength, 2 Stamina, 3 Technique, 4 Passing, 5 Dribbling,
        // 6 Shooting, 7 Defending, 8 Positioning, 9 Goalkeeping.

        /// <summary>Per-skill growth weight of each <see cref="TeamTrainingFocus"/> (index = enum value).</summary>
        private static readonly int[,] TeamWeights =
        {
            //               Pac Str Sta Tec Pas Dri Sho Def Pos  Gk
            /* Balanced  */ { 40, 40, 40, 40, 40, 40, 40, 40, 40, 40 },
            /* Attacking */ { 30, 10, 15, 50, 25, 80, 90, 10, 60, 10 },
            /* Defending */ { 20, 60, 30, 10, 20, 10, 10, 90, 80, 10 },
            /* Physical  */ { 80, 80, 90, 10, 10, 15, 15, 20, 15, 10 },
            /* Technical */ { 20, 10, 15, 90, 90, 70, 25, 15, 20, 10 },
            /* Tactical  */ { 15, 15, 15, 15, 15, 15, 15, 15, 15, 15 }
        };

        /// <summary>Extra per-skill weight an <see cref="IndividualTrainingFocus"/> adds on top of the team focus.</summary>
        private static readonly int[,] IndividualWeights =
        {
            //               Pac Str Sta Tec Pas Dri Sho Def Pos  Gk
            /* None      */ {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 },
            /* Attacking */ { 15,  0,  5, 25, 10, 40, 45,  0, 30,  0 },
            /* Defending */ { 10, 30, 15,  0, 10,  0,  0, 45, 40,  0 },
            /* Physical  */ { 40, 40, 45,  0,  0,  5,  5, 10,  5,  0 },
            /* Technical */ { 10,  0,  5, 45, 45, 35, 10,  5, 10,  0 }
        };

        /// <summary>The combined per-skill training weight for a team + individual focus pairing.</summary>
        public static int[] CombinedWeights(TeamTrainingFocus team, IndividualTrainingFocus individual)
        {
            int t = (int)team;
            int i = (int)individual;
            var weights = new int[PlayerAttributes.SkillCount];
            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
                weights[s] = TeamWeights[t, s] + IndividualWeights[i, s];
            return weights;
        }

        /// <summary>
        /// Familiarity points the club gains for its current tactic from one week of this
        /// team focus (only <see cref="TeamTrainingFocus.Tactical"/> gains; others 0).
        /// The host applies it to its stored familiarity level (see Tactics.FamiliarityModel).
        /// </summary>
        public static int TacticalFamiliarityGain(TeamTrainingFocus team, DevelopmentBalance cfg) =>
            team == TeamTrainingFocus.Tactical ? cfg.TacticalFocusFamiliarityGainPerWeek : 0;

        /// <summary>
        /// Applies one training week to a single player, mutating his attributes.
        /// Draws exactly <see cref="PlayerAttributes.SkillCount"/> values from
        /// <paramref name="rng"/> (one per skill) so the stream stays aligned.
        /// </summary>
        public static void ApplyTrainingWeek(
            Player player, TeamTrainingFocus team, IndividualTrainingFocus individual,
            IRandomSource rng, DevelopmentBalance cfg)
        {
            int[] weights = CombinedWeights(team, individual);
            int potential = player.Development.Potential;

            if (potential - PlayerRating.Overall(player) > 0)
                Grow(player, weights, potential, rng, cfg);
            else
                Decline(player, weights, potential, rng, cfg);
        }

        /// <summary>
        /// Improvement regime: focused skills rise, faster with more headroom. Growth is
        /// gated per increment against current overall, so a player never grows past his
        /// <paramref name="potential"/> (a strict ceiling), and slows as he approaches it.
        /// </summary>
        private static void Grow(Player player, int[] weights, int potential, IRandomSource rng, DevelopmentBalance cfg)
        {
            int cap = cfg.HeadroomScaleCap > 0 ? cfg.HeadroomScaleCap : 1;
            PlayerAttributes a = player.Attributes;

            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
            {
                int gap = potential - PlayerRating.Overall(player);   // recomputed: stops exactly at potential
                int headroom = gap < cap ? gap : cap;
                if (headroom < 0) headroom = 0;

                int permille = weights[s] * cfg.GrowthPerMillePerWeight / 100;
                permille = permille * headroom / cap;                 // slow as the player nears potential
                if (permille > cfg.GrowthMaxPerMille) permille = cfg.GrowthMaxPerMille;

                bool roll = rng.NextInt(0, 1000) < permille;          // always draw, for stream stability
                if (roll && gap > 0)
                    a[s] = a[s] + 1;
            }
        }

        /// <summary>
        /// Decline regime (overall at/above potential): each skill may lose a point, but
        /// only while the player's overall stays at or above its capped floor
        /// (<paramref name="potential"/> − DeclineFloorPoints). A skill drilled hard enough
        /// to clear the protection threshold declines slower (drilling keeps it sharp).
        /// </summary>
        private static void Decline(Player player, int[] weights, int potential, IRandomSource rng, DevelopmentBalance cfg)
        {
            int floor = potential - cfg.DeclineFloorPoints;
            PlayerAttributes a = player.Attributes;

            for (int s = 0; s < PlayerAttributes.SkillCount; s++)
            {
                int permille = cfg.DeclinePerMille;
                if (weights[s] >= cfg.DeclineProtectionWeightThreshold)   // genuinely drilled => protected
                    permille = permille * (100 - cfg.DeclineProtectionPercent) / 100;

                bool roll = rng.NextInt(0, 1000) < permille;          // always draw, for stream stability
                if (roll && PlayerRating.Overall(player) > floor && a[s] > AttributeScale.MinSkill)
                    a[s] = a[s] - 1;
            }
        }
    }
}
