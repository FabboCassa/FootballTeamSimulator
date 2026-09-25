using Sim.Core.Config;

namespace Sim.Core.Match.Movement
{
    /// <summary>Where an attacker stands for a corner (watchable-match spec, R6).</summary>
    public enum CornerRole
    {
        None = 0,
        NearPost = 1,
        FarPost = 2,
        EdgeOfBox = 3
    }

    /// <summary>
    /// Who attacks a corner, and from where. Pure and draw-free: the best man in the air goes to
    /// the far post, the next best to the near post, and the best finisher left waits on the
    /// edge of the box for the ball that is cleared or cut back.
    /// </summary>
    public static class CornerRoles
    {
        /// <param name="aerial">How good each man is in the air.</param>
        /// <param name="finishing">How good each man is at striking a loose ball.</param>
        /// <param name="eligible">False for the keeper, the taker and anybody sent off.</param>
        /// <param name="roles">Filled with each man's role; <see cref="CornerRole.None"/> for the rest.</param>
        public static void Assign(int[] aerial, int[] finishing, bool[] eligible, CornerRole[] roles)
        {
            for (int i = 0; i < roles.Length; i++) roles[i] = CornerRole.None;

            Take(aerial, eligible, roles, CornerRole.FarPost);
            Take(aerial, eligible, roles, CornerRole.NearPost);
            Take(finishing, eligible, roles, CornerRole.EdgeOfBox);
        }

        /// <summary>
        /// The spot for a role, in decimetres. <paramref name="lowY"/> is true when the corner is
        /// taken from the y = 0 side; the near post is the post on that side.
        /// </summary>
        public static void Spot(CornerRole role, bool attackingHome, bool lowY, MatchBalance cfg, out int xDm, out int yDm)
        {
            int goalX = MovementGeometry.AttackedGoalX(attackingHome);
            int dir = MovementGeometry.Direction(attackingHome);
            int toCorner = lowY ? -1 : 1;

            switch (role)
            {
                case CornerRole.NearPost:
                    xDm = goalX - dir * cfg.CornerNearPostDepthDm;
                    yDm = Pitch.CenterY + toCorner * cfg.CornerPostOffsetDm;
                    return;
                case CornerRole.FarPost:
                    xDm = goalX - dir * cfg.CornerFarPostDepthDm;
                    yDm = Pitch.CenterY - toCorner * cfg.CornerPostOffsetDm;
                    return;
                default:
                    xDm = goalX - dir * cfg.CornerEdgeDepthDm;
                    yDm = Pitch.CenterY;
                    return;
            }
        }

        private static void Take(int[] skill, bool[] eligible, CornerRole[] roles, CornerRole role)
        {
            int best = -1;
            for (int i = 0; i < roles.Length; i++)
            {
                if (!eligible[i] || roles[i] != CornerRole.None) continue;
                if (best < 0 || skill[i] > skill[best]) best = i;
            }

            if (best >= 0) roles[best] = role;
        }
    }
}
